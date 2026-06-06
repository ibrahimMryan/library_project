using Microsoft.Data.SqlClient;

namespace DataAccessLayer
{
    public static class DbConnection
    {
        private static readonly string _connectionString =
            "Server=.;Database=LibraryDB;Trusted_Connection=True;TrustServerCertificate=True;";

        public static SqlConnection GetConnection()
        {
            return new SqlConnection(_connectionString);
        }
    }
}