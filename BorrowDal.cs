using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace DataAccessLayer
{
    public class BorrowDal
    {
        public const decimal RentalFee = 1m;
        public const decimal DepositAmount = 5m;

        public int BorrowBook(int userId, int bookId, int dueDays, decimal fineAmount)
        {
            int newBorrowId = -1;

            using (SqlConnection conn = DbConnection.GetConnection())
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        string decreaseStock = @"
                    UPDATE Books
                    SET    AvailableQuantity = AvailableQuantity - 1
                    WHERE  BookID = @BookID
                      AND  AvailableQuantity > 0";

                        using (SqlCommand cmd1 = new SqlCommand(decreaseStock, conn, transaction))
                        {
                            cmd1.Parameters.AddWithValue("@BookID", bookId);
                            if (cmd1.ExecuteNonQuery() == 0)
                                throw new InvalidOperationException(
                                    "Sorry, this book just went out of stock.");
                        }

                        string insertBorrow = @"
                    INSERT INTO BorrowRecords 
                        (UserID, BookID, BorrowDate, DueDate, Status, IsPurchased)
                    VALUES 
                        (@UserID, @BookID, GETDATE(), 
                         DATEADD(day, @DueDays, GETDATE()), 'Borrowed', 0);
                    SELECT SCOPE_IDENTITY();";

                        using (SqlCommand cmd2 = new SqlCommand(insertBorrow, conn, transaction))
                        {
                            cmd2.Parameters.AddWithValue("@UserID", userId);
                            cmd2.Parameters.AddWithValue("@BookID", bookId);
                            cmd2.Parameters.AddWithValue("@DueDays", dueDays);
                            newBorrowId = Convert.ToInt32(cmd2.ExecuteScalar());
                        }

                        string insertRentalFee = @"
                    INSERT INTO Payments 
                        (BorrowID, UserID, Amount, PaymentStatus, CreatedDate, PaymentType)
                    VALUES 
                        (@BorrowID, @UserID, @Amount, 'Paid', GETDATE(), 'RentalFee')";

                        using (SqlCommand cmd3 = new SqlCommand(insertRentalFee, conn, transaction))
                        {
                            cmd3.Parameters.AddWithValue("@BorrowID", newBorrowId);
                            cmd3.Parameters.AddWithValue("@UserID", userId);
                            cmd3.Parameters.AddWithValue("@Amount", BorrowDal.RentalFee);
                            cmd3.ExecuteNonQuery();
                        }

                        string insertDeposit = @"
                    INSERT INTO Payments 
                        (BorrowID, UserID, Amount, PaymentStatus, CreatedDate, PaymentType)
                    VALUES 
                        (@BorrowID, @UserID, @Amount, 'Paid', GETDATE(), 'Deposit')";

                        using (SqlCommand cmd4 = new SqlCommand(insertDeposit, conn, transaction))
                        {
                            cmd4.Parameters.AddWithValue("@BorrowID", newBorrowId);
                            cmd4.Parameters.AddWithValue("@UserID", userId);
                            cmd4.Parameters.AddWithValue("@Amount", BorrowDal.DepositAmount);
                            cmd4.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch (InvalidOperationException)
                    {
                        transaction.Rollback();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"[BorrowBook] Rolled back: {ex.Message}");
                        throw new Exception("Something went wrong while borrowing. Please try again.");
                    }
                }
            }

            return newBorrowId;
        }

        public bool ReturnBook(int borrowId)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                conn.Open();

                int bookId = -1;
                bool isLate = false;
                int daysLate = 0;
                decimal dailyFineRate = 0m;

                // Step 1: Get borrow details + book's daily fine rate
                string getBorrow = @"
            SELECT br.BookID,
                   ISNULL(b.DailyLateFee, 1.00) AS DailyFineRate,
                   CASE WHEN GETDATE() > br.DueDate THEN 1 ELSE 0 END AS IsLate,
                   CASE WHEN GETDATE() > br.DueDate 
                        THEN DATEDIFF(day, br.DueDate, GETDATE()) 
                        ELSE 0 
                   END AS DaysLate
            FROM   BorrowRecords br
            JOIN   Books b ON br.BookID = b.BookID
            WHERE  br.BorrowID = @BorrowID
              AND  br.Status IN ('Borrowed', 'Overdue')";

                using (SqlCommand cmdGet = new SqlCommand(getBorrow, conn))
                {
                    cmdGet.Parameters.AddWithValue("@BorrowID", borrowId);
                    using (SqlDataReader reader = cmdGet.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new InvalidOperationException(
                                "Borrow record not found or already returned.");

                        bookId = reader.GetInt32(0);
                        dailyFineRate = reader.GetDecimal(1);
                        isLate = reader.GetInt32(2) == 1;
                        daysLate = reader.GetInt32(3);
                    }
                }

                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // Step 2: Mark borrow as returned
                        string updateBorrow = @"
                    UPDATE BorrowRecords
                    SET    ReturnDate = GETDATE(),
                           Status     = 'Returned'
                    WHERE  BorrowID   = @BorrowID";

                        using (SqlCommand cmd2 = new SqlCommand(updateBorrow, conn, transaction))
                        {
                            cmd2.Parameters.AddWithValue("@BorrowID", borrowId);
                            cmd2.ExecuteNonQuery();
                        }

                        // Step 3: Return book to stock
                        string increaseStock = @"
                    UPDATE Books
                    SET    AvailableQuantity = AvailableQuantity + 1
                    WHERE  BookID = @BookID";

                        using (SqlCommand cmd3 = new SqlCommand(increaseStock, conn, transaction))
                        {
                            cmd3.Parameters.AddWithValue("@BookID", bookId);
                            cmd3.ExecuteNonQuery();
                        }

                        if (!isLate)
                        {
                            // Returned on time — refund the deposit
                            string refundDeposit = @"
                        UPDATE Payments
                        SET    PaymentStatus = 'Refunded'
                        WHERE  BorrowID      = @BorrowID
                          AND  PaymentType   = 'Deposit'
                          AND  PaymentStatus = 'Paid'";

                            using (SqlCommand cmd4 = new SqlCommand(refundDeposit, conn, transaction))
                            {
                                cmd4.Parameters.AddWithValue("@BorrowID", borrowId);
                                cmd4.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            // Returned late:
                            // - Keep deposit as Unpaid (they forfeit it)
                            // - Insert a new fine = daysLate × dailyFineRate
                            decimal totalFine = daysLate * dailyFineRate;

                            string markDepositUnpaid = @"
                        UPDATE Payments
                        SET    PaymentStatus = 'Unpaid'
                        WHERE  BorrowID      = @BorrowID
                          AND  PaymentType   = 'Deposit'";

                            using (SqlCommand cmd4 = new SqlCommand(markDepositUnpaid, conn, transaction))
                            {
                                cmd4.Parameters.AddWithValue("@BorrowID", borrowId);
                                cmd4.ExecuteNonQuery();
                            }

                            // Only insert late fine if it's more than 0
                            if (totalFine > 0)
                            {
                                // Get UserID from borrow record
                                int userId = 0;
                                string getUserId = "SELECT UserID FROM BorrowRecords WHERE BorrowID = @BorrowID";
                                using (SqlCommand cmdUser = new SqlCommand(getUserId, conn, transaction))
                                {
                                    cmdUser.Parameters.AddWithValue("@BorrowID", borrowId);
                                    userId = Convert.ToInt32(cmdUser.ExecuteScalar());
                                }

                                string insertFine = @"
                            INSERT INTO Payments 
                                (BorrowID, UserID, Amount, PaymentStatus, CreatedDate, PaymentType)
                            VALUES 
                                (@BorrowID, @UserID, @Amount, 'Unpaid', GETDATE(), 'LateFine')";

                                using (SqlCommand cmd5 = new SqlCommand(insertFine, conn, transaction))
                                {
                                    cmd5.Parameters.AddWithValue("@BorrowID", borrowId);
                                    cmd5.Parameters.AddWithValue("@UserID", userId);
                                    cmd5.Parameters.AddWithValue("@Amount", totalFine);
                                    cmd5.ExecuteNonQuery();
                                }
                            }
                        }

                        transaction.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"[ReturnBook] Rolled back: {ex.Message}");
                        throw;
                    }
                }
            }
        }

        public bool BuyBook(int borrowId, decimal purchasePrice)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        string updateBorrow = @"
                            UPDATE BorrowRecords SET IsPurchased = 1, Status = 'Returned', ReturnDate = GETDATE()
                            WHERE BorrowID = @BorrowID";

                        using (SqlCommand cmd1 = new SqlCommand(updateBorrow, conn, transaction))
                        {
                            cmd1.Parameters.AddWithValue("@BorrowID", borrowId);
                            cmd1.ExecuteNonQuery();
                        }

                        string insertPurchase = @"
                            INSERT INTO Payments (BorrowID, UserID, Amount, PaymentStatus, CreatedDate, PaymentType)
                            SELECT BorrowID, UserID, @Price, 'Paid', GETDATE(), 'Purchase'
                            FROM BorrowRecords WHERE BorrowID = @BorrowID";

                        using (SqlCommand cmd2 = new SqlCommand(insertPurchase, conn, transaction))
                        {
                            cmd2.Parameters.AddWithValue("@BorrowID", borrowId);
                            cmd2.Parameters.AddWithValue("@Price", purchasePrice);
                            cmd2.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        return true;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public bool PayFine(int borrowId)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
                    UPDATE Payments SET PaymentStatus = 'Paid'
                    WHERE BorrowID = @BorrowID AND PaymentStatus = 'Unpaid'";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@BorrowID", borrowId);
                    try
                    {
                        conn.Open();
                        return cmd.ExecuteNonQuery() > 0;
                    }
                    catch { return false; }
                }
            }
        }

        public DataTable GetUserBorrowHistory(int userId)
        {
            DataTable dt = new DataTable();
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                MarkOverdueRecords(conn);

                string query = @"
            SELECT br.BorrowID, br.BookID,
                   b.Title     AS BookTitle,
                   br.BorrowDate, br.DueDate, br.ReturnDate,
                   br.Status,  br.IsPurchased,
                   ISNULL(b.PurchasePrice, 0) AS PurchasePrice,
                   ISNULL(b.Price, 0)         AS RentPrice,
                   ISNULL((SELECT SUM(Amount) FROM Payments p 
                           WHERE p.BorrowID = br.BorrowID 
                             AND p.PaymentStatus = 'Paid'), 0)     AS TotalPaid,
                   ISNULL((SELECT SUM(Amount) FROM Payments p 
                           WHERE p.BorrowID = br.BorrowID 
                             AND p.PaymentStatus = 'Unpaid'), 0)   AS UnpaidAmount,
                   ISNULL((SELECT SUM(Amount) FROM Payments p 
                           WHERE p.BorrowID = br.BorrowID 
                             AND p.PaymentStatus = 'Refunded'), 0) AS RefundedAmount
            FROM   BorrowRecords br
            JOIN   Books b ON br.BookID = b.BookID
            WHERE  br.UserID = @UserID
            ORDER BY br.BorrowDate DESC";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    try
                    {
                        conn.Open();
                        using (SqlDataReader reader = cmd.ExecuteReader())
                            dt.Load(reader);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GetUserBorrowHistory] DB error: {ex.Message}");
                    }
                }
            }
            return dt;
        }


        private void MarkOverdueRecords(SqlConnection conn)
        {
            string query = @"
        UPDATE BorrowRecords
        SET    Status = 'Overdue'
        WHERE  Status  = 'Borrowed'
          AND  DueDate < GETDATE()";

            bool wasOpen = conn.State == System.Data.ConnectionState.Open;

            if (!wasOpen) conn.Open();

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.ExecuteNonQuery();
            }

            if (!wasOpen) conn.Close();
        }

        public DataTable GetAllActiveBorrows()
        {
            DataTable dt = new DataTable();
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                MarkOverdueRecords(conn);

                string query = @"
            SELECT br.BorrowID, br.BookID, u.FullName AS MemberName, b.Title AS BookTitle,
                   br.BorrowDate, br.DueDate, br.Status,
                   ISNULL((SELECT SUM(p.Amount) FROM Payments p 
                           WHERE p.BorrowID = br.BorrowID 
                             AND p.PaymentStatus = 'Unpaid'), 0) AS UnpaidAmount
            FROM   BorrowRecords br
            JOIN   Users u ON br.UserID = u.UserID
            JOIN   Books b ON br.BookID = b.BookID
            WHERE  br.Status IN ('Borrowed', 'Overdue')
            ORDER BY br.DueDate ASC";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    try
                    {
                        conn.Open();
                        using (SqlDataReader reader = cmd.ExecuteReader())
                            dt.Load(reader);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GetAllActiveBorrows] DB error: {ex.Message}");
                    }
                }
            }
            return dt;
        }

        public DataTable GetAllBorrows()
        {
            DataTable dt = new DataTable();
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                MarkOverdueRecords(conn);

                string query = @"
            SELECT br.BorrowID, br.BookID,
                   u.FullName  AS MemberName,
                   b.Title     AS BookTitle,
                   br.BorrowDate, br.DueDate, br.ReturnDate,
                   br.Status,  br.IsPurchased,
                   ISNULL((SELECT SUM(Amount) FROM Payments p 
                           WHERE p.BorrowID = br.BorrowID 
                             AND p.PaymentStatus = 'Paid'), 0)     AS TotalPaid,
                   ISNULL((SELECT SUM(Amount) FROM Payments p 
                           WHERE p.BorrowID = br.BorrowID 
                             AND p.PaymentStatus = 'Unpaid'), 0)   AS UnpaidAmount,
                   ISNULL((SELECT SUM(Amount) FROM Payments p 
                           WHERE p.BorrowID = br.BorrowID 
                             AND p.PaymentStatus = 'Refunded'), 0) AS RefundedAmount
            FROM   BorrowRecords br
            JOIN   Users u ON br.UserID = u.UserID
            JOIN   Books b ON br.BookID = b.BookID
            ORDER BY br.BorrowDate DESC";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    try
                    {
                        conn.Open();
                        using (SqlDataReader reader = cmd.ExecuteReader())
                            dt.Load(reader);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GetAllBorrows] DB error: {ex.Message}");
                    }
                }
            }
            return dt;
        }

        public DataTable GetBorrowById(int borrowId)
        {
            DataTable dt = new DataTable();
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
            SELECT br.BorrowID,
                   br.UserID,
                   br.BookID,
                   br.BorrowDate,
                   br.DueDate,
                   br.ReturnDate,
                   br.Status,
                   ISNULL((SELECT SUM(Amount) FROM Payments p 
                           WHERE p.BorrowID = br.BorrowID 
                             AND p.PaymentStatus = 'Unpaid'), 0) AS FineAmount,
                   CASE 
                       WHEN EXISTS (SELECT 1 FROM Payments p 
                                   WHERE p.BorrowID = br.BorrowID 
                                     AND p.PaymentStatus = 'Unpaid') 
                       THEN 'Unpaid' 
                       ELSE 'Paid' 
                   END AS PaymentStatus
            FROM   BorrowRecords br
            WHERE  br.BorrowID = @BorrowID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@BorrowID", borrowId);
                    try
                    {
                        conn.Open();
                        using (SqlDataReader reader = cmd.ExecuteReader())
                            dt.Load(reader);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GetBorrowById] DB error: {ex.Message}");
                        throw;
                    }
                }
            }
            return dt;
        }

        public bool UserHasUnpaidPayments(int userId)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
                    SELECT COUNT(*) FROM Payments p
                    JOIN BorrowRecords br ON p.BorrowID = br.BorrowID
                    WHERE br.UserID = @UserID AND p.PaymentStatus = 'Unpaid'";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    try { conn.Open(); return (int)cmd.ExecuteScalar() > 0; }
                    catch { return false; }
                }
            }
        }

        public int GetActiveBorrowCount(int userId)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = "SELECT COUNT(*) FROM BorrowRecords WHERE UserID = @UserID AND Status IN ('Borrowed', 'Overdue')";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    try { conn.Open(); return (int)cmd.ExecuteScalar(); }
                    catch { return 0; }
                }
            }
        }

        public bool HasActiveBorrowForBook(int userId, int bookId)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = "SELECT COUNT(*) FROM BorrowRecords WHERE UserID = @UserID AND BookID = @BookID AND Status IN ('Borrowed', 'Overdue')";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    cmd.Parameters.AddWithValue("@BookID", bookId);
                    try { conn.Open(); return (int)cmd.ExecuteScalar() > 0; }
                    catch { return false; }
                }
            }
        }
    }
}