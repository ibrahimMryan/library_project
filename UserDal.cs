using System;
using System.Data;
using Microsoft.Data.SqlClient;
using BCrypt.Net;

namespace DataAccessLayer
{
    public class UserDal
    {
        private const int DefaultMemberRoleId = 2;

        public int RegisterUser(string fullName, string email, string password)
        {
            string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);
            int newUserId = -1;

            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
                    INSERT INTO Users (FullName, Email, Password, RoleID)
                    VALUES (@FullName, @Email, @Password, @DefaultRoleID);
                    SELECT SCOPE_IDENTITY();";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@FullName", fullName);
                    cmd.Parameters.AddWithValue("@Email", email);
                    cmd.Parameters.AddWithValue("@Password", hashedPassword);
                    cmd.Parameters.AddWithValue("@DefaultRoleID", DefaultMemberRoleId);

                    try
                    {
                        conn.Open();
                        object result = cmd.ExecuteScalar();

                        if (int.TryParse(result?.ToString(), out int id))
                        {
                            newUserId = id;
                        }
                    }
                    catch (SqlException ex)
                    {
                        if (ex.Number == 2627 || ex.Number == 2601)
                            throw new InvalidOperationException("This email is already registered.", ex);

                        throw;
                    }
                }
            }

            return newUserId;
        }

        public DataTable GetUserById(int userId)
        {
            DataTable dt = new DataTable();
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
                    SELECT UserID, FullName, Email, RoleID
                    FROM   Users
                    WHERE  UserID = @UserID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);
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
                        Console.WriteLine($"[GetUserById] DB error: {ex.Message}");
                        throw;
                    }
                }
            }
            return dt;
        }

        public DataTable LoginUser(string email, string password)
        {
            DataTable dt = new DataTable();

            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = "SELECT UserID, FullName, Email, Password, RoleID FROM Users WHERE Email = @Email";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Email", email);

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
                        Console.WriteLine($"[LoginUser] DB error: {ex.Message}");
                        return new DataTable();
                    }
                }
            }

            if (dt.Rows.Count == 0)
                return new DataTable();

            string storedHash = dt.Rows[0]["Password"].ToString().Trim();
            bool passwordValid = false;

            try
            {
                if (storedHash.StartsWith("$2"))
                {
                    passwordValid = BCrypt.Net.BCrypt.Verify(password, storedHash);
                }
                else
                {
                    if (storedHash == password)
                    {
                        passwordValid = true;
                        UpgradeToBCrypt(Convert.ToInt32(dt.Rows[0]["UserID"]), password);
                    }
                }
            }
            catch (Exception)
            {
                return new DataTable();
            }

            if (!passwordValid)
                return new DataTable();

            dt.Columns.Remove("Password");
            return dt;
        }

        private void UpgradeToBCrypt(int userId, string plainPassword)
        {
            string hashed = BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 12);

            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = "UPDATE Users SET Password = @Password WHERE UserID = @UserID";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@Password", hashed);
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    try
                    {
                        conn.Open();
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UpgradeToBCrypt] Silent failure upgrading user password: {ex.Message}");
                    }
                }
            }
        }

        public DataTable GetAllUsers()
        {
            DataTable dt = new DataTable();
            using(SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
                    SELECT u.UserID, u.FullName, u.Email, u.RoleID, r.RoleName
                    FROM   Users u
                    JOIN   Roles r ON u.RoleID = r.RoleID
                    ORDER BY u.FullName";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
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
                        Console.WriteLine($"[GetAllUsers] DB error: {ex.Message}");
                        return new DataTable();
                    }
                    return dt;
                }
            }
        }


      
        public bool UpdateUserRole(int userId, int newRoleId)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = "UPDATE Users SET RoleID = @RoleID WHERE UserID = @UserID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@RoleID", newRoleId);
                    cmd.Parameters.AddWithValue("@UserID", userId);

                    try
                    {
                        conn.Open();
                        return cmd.ExecuteNonQuery() > 0;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UpdateUserRole] DB error: {ex.Message}");
                        return false;
                    }
                }
            }
        }

        public bool UpdateUser(int userId, string fullName, string email)
        {
            using (SqlConnection conn = DbConnection.GetConnection())
            {
                string query = @"
                    UPDATE Users
                    SET FullName = @FullName,
                        Email  = @Email
                    WHERE UserID = @UserID";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    cmd.Parameters.AddWithValue("@FullName", fullName);
                    cmd.Parameters.AddWithValue("@Email", email);

                    try
                    {
                        conn.Open();
                        return cmd.ExecuteNonQuery() > 0;
                    }
                    catch (SqlException ex)
                    {
                        if (ex.Number == 2627 || ex.Number == 2601)
                            throw new InvalidOperationException("This email is already in use.", ex);

                        Console.WriteLine($"[UpdateUser] DB error: {ex.Message}");
                        throw;
                    }
                }
            }
        }
    }
}