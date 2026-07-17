using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace SqlToLinq.Tests {

    public record User(
        int Id,
        string Name,
        int Age,
        string Role,
        int Points,
        int Bonus
    );

    public record Order(
        int Id,
        int Owner,
        string Item,
        int Qty
    );

    public record Product(
        int Id,
        int Parent,
        string Title,
        int Price
    );

    public class TestDbContext : DbContext {

        public DbSet<User> Users { get; set; }

        public DbSet<Order> Orders { get; set; }

        public DbSet<Product> Products { get; set; }

        private readonly SqliteConnection _connection;

        public TestDbContext(SqliteConnection connection) {
            _connection = connection;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
            optionsBuilder.UseSqlite(_connection);
        }
    }

    public static class TestSeedData {
        public static void Seed(TestDbContext db) {

            db.Users.AddRange(
                new User(1, "Bob", 25, "Admin", 100, 10),
                new User(2, "Bab", 30, "User", 50, 5),
                new User(3, "Bcb", 17, "User", 20, 0),
                new User(4, "bob", 40, "Moderator", 80, 15),
                new User(5, "B.b", 22, "User", 10, 0),
                new User(6, "Alice", 19, "Admin", 200, 50)
            );

            db.Orders.AddRange(
                new Order(1, 1, "Laptop", 1),
                new Order(2, 1, "Mouse", 2),
                new Order(3, 2, "Keyboard", 1),
                new Order(4, 6, "Monitor", 1)
            );

            db.Products.AddRange(
                new Product(1, 1, "Keyboard Skin", 15),
                new Product(2, 1, "Mouse Pad", 8),
                new Product(3, 3, "USB Cable", 5),
                new Product(4, 4, "HDMI Cable", 12)
            );

            db.SaveChanges();
        }
    }
}