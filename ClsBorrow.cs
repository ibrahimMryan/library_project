using BusinessLogicLayer;
using BusnissLogicLayer;
using DataAccessLayer;
using System;
using System.Data;

namespace BusnissLogicLayer
{
    public class ClsBorrow
    {
        public enum enMode { AddNew = 0, Update = 1 }
        public enMode Mode = enMode.AddNew;

        public int BorrowId { get; set; }
        public int UserId { get; set; }
        public int BookId { get; set; }
        public DateTime BorrowDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public string Status { get; set; }
        public decimal Amount { get; set; }
        public string PaymentStatus { get; set; }

        public static readonly int DefaultDueDays = 14;
        public static readonly decimal DefaultFineAmount = 1m;

        private static BorrowDal _borrowDal = new BorrowDal();

        public ClsBorrow()
        {
            BorrowId = 0;
            UserId = 0;
            BookId = 0;
            BorrowDate = DateTime.Now;
            DueDate = DateTime.Now.AddDays(DefaultDueDays);
            ReturnDate = null;
            Status = "Borrowed";
            PaymentStatus = "Unpaid";
            Amount = DefaultFineAmount;
            Mode = enMode.AddNew;
        }

        public ClsBorrow(int borrowId, int userId, int bookId, DateTime borrowDate,
                         DateTime dueDate, DateTime? returnDate, string status,
                         decimal amount, string paymentStatus)
        {
            BorrowId = borrowId;
            UserId = userId;
            BookId = bookId;
            BorrowDate = borrowDate;
            DueDate = dueDate;
            ReturnDate = returnDate;
            Status = status;
            Amount = amount;
            PaymentStatus = paymentStatus;
            Mode = enMode.Update;
        }

        public static ClsBorrow FindByBorrowId(int borrowId)
        {
            DataTable dt = _borrowDal.GetBorrowById(borrowId);

            if (dt.Rows.Count == 0)
                return null;

            DataRow row = dt.Rows[0];

            return new ClsBorrow(
                (int)row["BorrowID"],
                (int)row["UserID"],
                (int)row["BookID"],
                (DateTime)row["BorrowDate"],
                (DateTime)row["DueDate"],
                row["ReturnDate"] == DBNull.Value ? null : (DateTime?)row["ReturnDate"],
                (string)row["Status"],
                row["FineAmount"] != DBNull.Value ? (decimal)row["FineAmount"] : 0m,
                row["PaymentStatus"] != DBNull.Value ? (string)row["PaymentStatus"] : "Unpaid"
            );
        }

        public bool IsOverdue()
        {
            return DateTime.Now > DueDate && Status == "Borrowed";
        }

        public static DataTable GetUserBorrowHistory(int userId)
            => _borrowDal.GetUserBorrowHistory(userId);

        public static DataTable GetAllActiveBorrows()
            => _borrowDal.GetAllActiveBorrows();

        public static DataTable GetAllBorrows()
            => _borrowDal.GetAllBorrows();

        public static bool ReturnBook(int borrowId)
            => _borrowDal.ReturnBook(borrowId);

        public static bool PayFine(int borrowId)
            => _borrowDal.PayFine(borrowId);

        public static bool BuyBook(int borrowId, decimal price)
            => _borrowDal.BuyBook(borrowId, price);

        private bool AddNewBorrow()
        {
            if (!ClsUser.IsUserExist(UserId))
                throw new Exception("User not found.");

            if (_borrowDal.HasActiveBorrowForBook(UserId, BookId))
                throw new InvalidOperationException("You already have this book borrowed.");

            if (_borrowDal.UserHasUnpaidPayments(UserId))
                throw new InvalidOperationException("You have unpaid fines. Please pay them first.");

            if (_borrowDal.GetActiveBorrowCount(UserId) >= 3)
                throw new InvalidOperationException("You can only borrow up to 3 books at a time.");

            ClsBook book = ClsBook.FindByBookId(BookId);
            if (book == null || book.AvailableQuantity <= 0)
                throw new InvalidOperationException("This book is currently out of stock.");

            BorrowId = _borrowDal.BorrowBook(UserId, BookId, DefaultDueDays, Amount);
            return BorrowId != -1;
        }

        private bool UpdateBorrow()
        {
            return _borrowDal.ReturnBook(BorrowId);
        }

        public bool Save()
        {
            if (BorrowId > 0)
                Mode = enMode.Update;

            switch (Mode)
            {
                case enMode.AddNew:
                    if (AddNewBorrow())
                    {
                        Mode = enMode.Update;
                        return true;
                    }
                    return false;

                case enMode.Update:
                    return UpdateBorrow();
            }

            return false;
        }
    }
}