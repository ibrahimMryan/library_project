using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace DataAccessLayer
{
    public class BookDal
    {
        public DataTable GetAllBooks()
        {
            DataTable dt = new DataTable();
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
            SELECT BookID, Title, Author, Category, Quantity,
                   AvailableQuantity, Price, 
                   ISNULL(DailyLateFee, 1.00)   AS DailyLateFee,
                   ISNULL(PurchasePrice, 0)      AS PurchasePrice
            FROM   Books
            ORDER BY Title";

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
                        Console.WriteLine($"[GetAllBooks] DB error: {ex.Message}");
                        throw;
                    }
                }
            }
            return dt;
        }


        public DataTable GetBookById(int bookId)
        {
            DataTable dt = new DataTable();
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
            SELECT BookID, Title, Author, Category, Quantity,
                   AvailableQuantity, Price,
                   ISNULL(DailyLateFee, 1.00)   AS DailyLateFee,
                   ISNULL(PurchasePrice, 0)      AS PurchasePrice
            FROM   Books
            WHERE  BookID = @BookID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@BookID", bookId);
                    try
                    {
                        conn.Open();
                        using (SqlDataReader reader = cmd.ExecuteReader())
                            dt.Load(reader);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GetBookById] DB error: {ex.Message}");
                        throw;
                    }
                }
            }
            return dt;
        }
        public int AddBook(string title, string author, string category,
                    int quantity, decimal rentFee, decimal dailyLateFee, decimal purchasePrice)
        {
            int newBookId = -1;
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
            INSERT INTO Books 
                (Title, Author, Category, Quantity, AvailableQuantity, 
                 Price, DailyLateFee, PurchasePrice)
            VALUES 
                (@Title, @Author, @Category, @Quantity, @Quantity,
                 @Price, @DailyLateFee, @PurchasePrice);
            SELECT SCOPE_IDENTITY();";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Title", title);
                    cmd.Parameters.AddWithValue("@Author", author);
                    cmd.Parameters.AddWithValue("@Category", category);
                    cmd.Parameters.AddWithValue("@Quantity", quantity);
                    cmd.Parameters.AddWithValue("@Price", rentFee);
                    cmd.Parameters.AddWithValue("@DailyLateFee", dailyLateFee);
                    cmd.Parameters.AddWithValue("@PurchasePrice", purchasePrice);

                    try
                    {
                        conn.Open();
                        if (int.TryParse(cmd.ExecuteScalar()?.ToString(), out int id))
                            newBookId = id;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AddBook] DB error: {ex.Message}");
                        throw;
                    }
                }
            }
            return newBookId;
        }
        public bool UpdateBook1(int bookId, string title, string author, string category,
                        int quantity, decimal rentFee, decimal dailyLateFee,
                        int availableQuantity, decimal purchasePrice)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
            UPDATE Books
            SET    Title             = @Title,
                   Author            = @Author,
                   Category          = @Category,
                   Quantity          = @Quantity,
                   AvailableQuantity = @AvailableQuantity,
                   Price             = @Price,
                   DailyLateFee      = @DailyLateFee,
                   PurchasePrice     = @PurchasePrice
            WHERE  BookID            = @BookID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@BookID", bookId);
                    cmd.Parameters.AddWithValue("@Title", title);
                    cmd.Parameters.AddWithValue("@Author", author);
                    cmd.Parameters.AddWithValue("@Category", category);
                    cmd.Parameters.AddWithValue("@Quantity", quantity);
                    cmd.Parameters.AddWithValue("@AvailableQuantity", availableQuantity);
                    cmd.Parameters.AddWithValue("@Price", rentFee);
                    cmd.Parameters.AddWithValue("@DailyLateFee", dailyLateFee);
                    cmd.Parameters.AddWithValue("@PurchasePrice", purchasePrice);

                    try
                    {
                        conn.Open();
                        return cmd.ExecuteNonQuery() > 0;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UpdateBook1] DB error: {ex.Message}");
                        return false;
                    }
                }
            }
        }

        public bool DeleteBook(int bookId)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                // Check if any copies are currently borrowed
                string checkQuery = @"
            SELECT COUNT(*) 
            FROM BorrowRecords 
            WHERE BookID = @BookID AND ReturnDate IS NULL AND IsPurchased = 0";

                using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@BookID", bookId);

                    try
                    {
                        conn.Open();
                        int borrowedCopies = Convert.ToInt32(checkCmd.ExecuteScalar());

                        if (borrowedCopies > 0)
                        {
                            throw new InvalidOperationException($"Cannot delete this book — {borrowedCopies} copy(ies) currently borrowed.");
                        }

                        // If no copies are borrowed, delete the book
                        string deleteQuery = "DELETE FROM Books WHERE BookID = @BookID";
                        using (SqlCommand deleteCmd = new SqlCommand(deleteQuery, conn))
                        {
                            deleteCmd.Parameters.AddWithValue("@BookID", bookId);
                            int rowsDeleted = deleteCmd.ExecuteNonQuery();

                            if (rowsDeleted == 0)
                            {
                                throw new InvalidOperationException("Book not found.");
                            }

                            return true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DeleteBook] DB error: {ex.Message}");
                        return false;
                    }
                }
            }
        }

        public DataTable GetBooksByCategory(string category)
        {
            DataTable dt = new DataTable();

            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
                    SELECT BookID, Title, Author, Category, Quantity, AvailableQuantity, Price
                    FROM   Books
                    WHERE  Category = @Category
                    ORDER BY Title";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Category", category);

                    try
                    {
                        conn.Open();
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            dt.Load(reader);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GetBooksByCategory] DB error: {ex.Message}");
                        throw;
                    }
                }
            }

            return dt;
        }

        public DataTable GetAvailableBooks()
        {
            DataTable dt = new DataTable();
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
            SELECT BookID, Title, Author, Category, Quantity,
                   AvailableQuantity, Price,
                   ISNULL(DailyLateFee, 1.00)   AS DailyLateFee,
                   ISNULL(PurchasePrice, 0)      AS PurchasePrice
            FROM   Books
            WHERE  AvailableQuantity > 0
            ORDER BY Title";

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
                        Console.WriteLine($"[GetAvailableBooks] DB error: {ex.Message}");
                        throw;
                    }
                }
            }
            return dt;
        }

        public int GetBorrowedCopies(int bookId)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
            SELECT Quantity - AvailableQuantity
            FROM Books
            WHERE BookID = @BookID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@BookID", bookId);

                    conn.Open();

                    object result = cmd.ExecuteScalar();

                    return result != null ? Convert.ToInt32(result) : 0;
                }
            }
        }

        public DataTable SearchBooks(string keyword)
        {
            DataTable dt = new DataTable();
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
            SELECT BookID, Title, Author, Category, Quantity, 
                   AvailableQuantity, Price, ISNULL(PurchasePrice, 0) AS PurchasePrice
            FROM Books
            WHERE AvailableQuantity > 0
            AND (Title LIKE @Keyword OR Author LIKE @Keyword OR Category LIKE @Keyword)
            ORDER BY Title";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");
                    conn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                        dt.Load(reader);
                }
            }
            return dt;
        }

        public bool BuyBookDirect(int userId, int bookId, decimal price)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // Create borrow record as purchased (use a future due date to satisfy constraint)
                        string insertBorrow = @"
                    INSERT INTO BorrowRecords (UserID, BookID, BorrowDate, DueDate, Status, IsPurchased)
                    VALUES (@UserID, @BookID, GETDATE(), DATEADD(day, 1, GETDATE()), 'Returned', 1);
                    SELECT SCOPE_IDENTITY();";

                        int borrowId = 0;
                        using (SqlCommand cmd1 = new SqlCommand(insertBorrow, conn, transaction))
                        {
                            cmd1.Parameters.AddWithValue("@UserID", userId);
                            cmd1.Parameters.AddWithValue("@BookID", bookId);
                            borrowId = Convert.ToInt32(cmd1.ExecuteScalar());
                        }

                        // Decrease stock (book is "used")
                        string decreaseStock = @"
                    UPDATE Books SET AvailableQuantity = AvailableQuantity - 1
                    WHERE BookID = @BookID AND AvailableQuantity > 0";

                        using (SqlCommand cmd2 = new SqlCommand(decreaseStock, conn, transaction))
                        {
                            cmd2.Parameters.AddWithValue("@BookID", bookId);
                            cmd2.ExecuteNonQuery();
                        }

                        // Create purchase payment
                        string insertPayment = @"
                    INSERT INTO Payments (BorrowID, UserID, Amount, PaymentStatus, CreatedDate, PaymentType)
                    VALUES (@BorrowID, @UserID, @Amount, 'Paid', GETDATE(), 'Purchase')";

                        using (SqlCommand cmd3 = new SqlCommand(insertPayment, conn, transaction))
                        {
                            cmd3.Parameters.AddWithValue("@BorrowID", borrowId);
                            cmd3.Parameters.AddWithValue("@UserID", userId);
                            cmd3.Parameters.AddWithValue("@Amount", price);
                            cmd3.ExecuteNonQuery();
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
    }
}