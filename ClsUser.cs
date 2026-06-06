using DataAccessLayer;
using System;
using System.Data;

namespace BusinessLogicLayer
{
    public class ClsUser
    {
        public enum enMode { AddNew = 0, Update = 1 }
        public enMode Mode = enMode.AddNew;

        public int UserId { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public int RoleId { get; set; }

        private static UserDal _userDal = new UserDal();

        public ClsUser()
        {
            UserId = 0;
            FullName = "";
            Email = "";
            Password = "";
            RoleId = 2;
            Mode = enMode.AddNew;
        }

        public ClsUser(int id, string name, string email, string pass, int role)
        {
            UserId = id;
            FullName = name;
            Email = email;
            Password = pass ?? "";
            RoleId = role;
            Mode = enMode.Update;
        }

        public static ClsUser FindByUserId(int id)
        {
            DataTable dt = _userDal.GetUserById(id);
            if (dt.Rows.Count == 0) return null;

            DataRow r = dt.Rows[0];

            return new ClsUser(
                r["UserID"] != DBNull.Value ? Convert.ToInt32(r["UserID"]) : 0,
                r["FullName"] != DBNull.Value ? r["FullName"].ToString() : "",
                r["Email"] != DBNull.Value ? r["Email"].ToString() : "",
                "",
                r["RoleID"] != DBNull.Value ? Convert.ToInt32(r["RoleID"]) : 2
            );
        }

        public static ClsUser Login(string email, string password)
        {
            DataTable dt = _userDal.LoginUser(email, password);
            if (dt.Rows.Count == 0) return null;

            DataRow r = dt.Rows[0];

            return new ClsUser(
                r["UserID"] != DBNull.Value ? Convert.ToInt32(r["UserID"]) : 0,
                r["FullName"] != DBNull.Value ? r["FullName"].ToString() : "",
                r["Email"] != DBNull.Value ? r["Email"].ToString() : "",
                "",
                r["RoleID"] != DBNull.Value ? Convert.ToInt32(r["RoleID"]) : 2
            );
        }

        public static DataTable GetAllUsers() => _userDal.GetAllUsers();

        public static bool IsUserExist(int id)
            => _userDal.GetUserById(id).Rows.Count > 0;

        public static bool UpdateUserRole(int userId, int roleId)
            => _userDal.UpdateUserRole(userId, roleId);

        private bool AddNewUser()
        {
            if (string.IsNullOrWhiteSpace(FullName))
                throw new Exception("Full name is required.");

            if (string.IsNullOrWhiteSpace(Email))
                throw new Exception("Email is required.");

            if (Password == null || Password.Length < 6)
                throw new Exception("Password must be at least 6 characters.");

            try { _ = new System.Net.Mail.MailAddress(Email); }
            catch { throw new Exception("Invalid email format."); }

            UserId = _userDal.RegisterUser(FullName, Email, Password);

            return UserId != -1;
        }

        private bool UpdateUser()
        {
            try { _ = new System.Net.Mail.MailAddress(Email); }
            catch { throw new Exception("Invalid email format."); }

            if (string.IsNullOrWhiteSpace(FullName))
                throw new Exception("Full name is required.");

            if (string.IsNullOrWhiteSpace(Email))
                throw new Exception("Email is required.");

            return _userDal.UpdateUser(UserId, FullName, Email);
        }

        public bool Save()
        {
            switch (Mode)
            {
                case enMode.AddNew:
                    if (AddNewUser())
                    {
                        Mode = enMode.Update;
                        return true;
                    }
                    return false;

                case enMode.Update:
                    return UpdateUser();
            }

            return false;
        }
    }
}