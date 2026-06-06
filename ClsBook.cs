using DataAccessLayer;
using System;
using System.Data;

namespace BusnissLogicLayer
{
    public class ClsBook
    {
        public enum enMode { AddNew = 0, Update = 1 }
        public enMode Mode { get; set; } = enMode.AddNew;

        public int BookId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Category { get; set; }
        public int Quantity { get; set; }
        public int AvailableQuantity { get; set; }
        public decimal Price { get; set; }          
        public decimal DailyLateFee { get; set; }   
        public decimal PurchasePrice { get; set; }  

        private static BookDal _bookDal = new BookDal();

        public ClsBook()
        {
            BookId = 0;
            Title = "";
            Author = "";
            Category = "";
            Quantity = 0;
            AvailableQuantity = 0;
            Price = 1.00m;
            DailyLateFee = 1.00m;
            PurchasePrice = 0m;
            Mode = enMode.AddNew;
        }

        public ClsBook(int id, string title, string author, string category,
                       int quantity, int available, decimal price,
                       decimal dailyLateFee, decimal purchasePrice)
        {
            BookId = id;
            Title = title;
            Author = author;
            Category = category;
            Quantity = quantity;
            AvailableQuantity = available;
            Price = price;
            DailyLateFee = dailyLateFee;
            PurchasePrice = purchasePrice;
            Mode = enMode.Update;
        }

        public static ClsBook FindByBookId(int id)
        {
            DataTable dt = _bookDal.GetBookById(id);
            if (dt.Rows.Count == 0) return null;

            DataRow r = dt.Rows[0];

            return new ClsBook(
                (int)r["BookID"],
                (string)r["Title"],
                (string)r["Author"],
                (string)r["Category"],
                (int)r["Quantity"],
                (int)r["AvailableQuantity"],
                r["Price"] != DBNull.Value ? Convert.ToDecimal(r["Price"]) : 1m,
                r["DailyLateFee"] != DBNull.Value ? Convert.ToDecimal(r["DailyLateFee"]) : 1m,
                r["PurchasePrice"] != DBNull.Value ? Convert.ToDecimal(r["PurchasePrice"]) : 0m
            );
        }

        public static DataTable GetAllBooks() => _bookDal.GetAllBooks();
        public static DataTable GetAvailableBooks() => _bookDal.GetAvailableBooks();
        public static bool DeleteBook(int id) => _bookDal.DeleteBook(id);
        public static DataTable SearchBooks(string keyword) => _bookDal.SearchBooks(keyword);
        public static bool BuyBookDirect(int userId, int bookId, decimal price)
            => _bookDal.BuyBookDirect(userId, bookId, price);

        private bool AddNewBook()
        {
            if (string.IsNullOrWhiteSpace(Title))
                throw new Exception("Title is required.");
            if (Quantity < 0)
                throw new Exception("Quantity cannot be negative.");

            int id = _bookDal.AddBook(Title, Author, Category, Quantity,
                                      Price, DailyLateFee, PurchasePrice);
            if (id == -1) return false;

            BookId = id;
            AvailableQuantity = Quantity;
            return true;
        }

        private bool UpdateBook()
        {
            int borrowed = _bookDal.GetBorrowedCopies(BookId);

            if (Quantity < borrowed)
                throw new Exception("Quantity cannot be less than currently borrowed copies.");

            AvailableQuantity = Quantity - borrowed;

            return _bookDal.UpdateBook1(
                BookId, Title, Author, Category,
                Quantity, Price, DailyLateFee,
                AvailableQuantity, PurchasePrice
            );
        }

        public bool Save()
        {
            if (BookId > 0)
                Mode = enMode.Update;

            switch (Mode)
            {
                case enMode.AddNew:
                    if (AddNewBook())
                    {
                        Mode = enMode.Update;
                        return true;
                    }
                    return false;

                case enMode.Update:
                    return UpdateBook();
            }

            return false;
        }
    }
}