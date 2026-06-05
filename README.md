1. SQL Stored Procedures Script
Run this script on your OrderDb database to create the required stored procedures. You can execute it using SQL Server Management Studio, Azure Data Studio, or the sqlcmd tool.

sql
USE [OrderDb]   -- or your actual database name
GO

-- Get all orders
CREATE OR ALTER PROCEDURE usp_GetOrders
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, ProductName, Quantity, Price, OrderDate
    FROM Orders;
END
GO

-- Get a single order by ID
CREATE OR ALTER PROCEDURE usp_GetOrderById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, ProductName, Quantity, Price, OrderDate
    FROM Orders
    WHERE Id = @Id;
END
GO

-- Create a new order and return the inserted row
CREATE OR ALTER PROCEDURE usp_CreateOrder
    @ProductName NVARCHAR(100),
    @Quantity INT,
    @Price DECIMAL(18,2),
    @NewOrderId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO Orders (ProductName, Quantity, Price, OrderDate)
    VALUES (@ProductName, @Quantity, @Price, GETUTCDATE());

    SET @NewOrderId = SCOPE_IDENTITY();

    -- Return the newly created record
    SELECT Id, ProductName, Quantity, Price, OrderDate
    FROM Orders
    WHERE Id = @NewOrderId;
END
GO

-- Update an existing order
CREATE OR ALTER PROCEDURE usp_UpdateOrder
    @Id INT,
    @ProductName NVARCHAR(100),
    @Quantity INT,
    @Price DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Orders
    SET ProductName = @ProductName,
        Quantity = @Quantity,
        Price = @Price
    WHERE Id = @Id;
END
GO

-- Delete an order
CREATE OR ALTER PROCEDURE usp_DeleteOrder
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM Orders WHERE Id = @Id;
END
GO
If your database was auto-created with EnsureCreated(), the Orders table already exists. The script uses CREATE OR ALTER so it can be re-run safely.

2. Automatically Create Stored Procedures on Startup (Optional)
If you prefer the procedures to be created automatically when the API starts, add the following helper method and call it in Program.cs. This keeps everything self-contained.

File: OrderApi/Data/OrderDbContext.cs (append the method)

csharp
using Microsoft.EntityFrameworkCore;
using OrderApi.Models;

namespace OrderApi.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    public void EnsureStoredProceduresCreated()
    {
        Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_GetOrders')
                EXEC('CREATE PROCEDURE usp_GetOrders AS BEGIN SELECT * FROM Orders END')

            IF NOT EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_GetOrderById')
                EXEC('CREATE PROCEDURE usp_GetOrderById @Id INT AS BEGIN SELECT * FROM Orders WHERE Id = @Id END')

            IF NOT EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_CreateOrder')
                EXEC('CREATE PROCEDURE usp_CreateOrder @ProductName NVARCHAR(100), @Quantity INT, @Price DECIMAL(18,2), @NewOrderId INT OUTPUT AS BEGIN INSERT INTO Orders (ProductName, Quantity, Price, OrderDate) VALUES (@ProductName, @Quantity, @Price, GETUTCDATE()); SET @NewOrderId = SCOPE_IDENTITY(); SELECT * FROM Orders WHERE Id = @NewOrderId; END')

            IF NOT EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_UpdateOrder')
                EXEC('CREATE PROCEDURE usp_UpdateOrder @Id INT, @ProductName NVARCHAR(100), @Quantity INT, @Price DECIMAL(18,2) AS BEGIN UPDATE Orders SET ProductName = @ProductName, Quantity = @Quantity, Price = @Price WHERE Id = @Id END')

            IF NOT EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'usp_DeleteOrder')
                EXEC('CREATE PROCEDURE usp_DeleteOrder @Id INT AS BEGIN DELETE FROM Orders WHERE Id = @Id END')
        ");
    }
}
Then modify the startup code in Program.cs:

csharp
// ... (existing code)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    db.Database.EnsureCreated();        // Creates the table if it doesn’t exist
    db.EnsureStoredProceduresCreated(); // Creates the stored procedures
}
app.Run();
This way, the procedures are created on first run. For production, you should use proper migrations.

3. Updated OrdersController (Using Stored Procedures)
Replace Controllers/OrdersController.cs with the version below. All CRUD endpoints now call the corresponding stored procedures via EF Core.

csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderApi.Data;
using OrderApi.Dtos;
using OrderApi.Models;

namespace OrderApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly OrderDbContext _context;

    public OrdersController(OrderDbContext context)
    {
        _context = context;
    }

    // GET: api/orders
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
    {
        // Execute stored procedure usp_GetOrders
        var orders = await _context.Orders
            .FromSqlRaw("EXEC usp_GetOrders")
            .ToListAsync();
        return orders;
    }

    // GET: api/orders/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrder(int id)
    {
        var orders = await _context.Orders
            .FromSqlInterpolated($"EXEC usp_GetOrderById @Id = {id}")
            .ToListAsync();

        var order = orders.FirstOrDefault();
        if (order == null)
            return NotFound();

        return order;
    }

    // POST: api/orders
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Output parameter to capture new order ID
        var newOrderIdParam = new SqlParameter("@NewOrderId", System.Data.SqlDbType.Int)
        {
            Direction = System.Data.ParameterDirection.Output
        };

        // Execute usp_CreateOrder
        await _context.Database.ExecuteSqlRawAsync(
            "EXEC usp_CreateOrder @ProductName, @Quantity, @Price, @NewOrderId OUTPUT",
            new SqlParameter("@ProductName", dto.ProductName),
            new SqlParameter("@Quantity", dto.Quantity),
            new SqlParameter("@Price", dto.Price),
            newOrderIdParam);

        int newOrderId = (int)newOrderIdParam.Value;

        // Retrieve the newly created order using the GetById stored procedure
        var order = await _context.Orders
            .FromSqlInterpolated($"EXEC usp_GetOrderById @Id = {newOrderId}")
            .ToListAsync()
            .ContinueWith(t => t.Result.FirstOrDefault());

        return CreatedAtAction(nameof(GetOrder), new { id = newOrderId }, order);
    }

    // PUT: api/orders/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateOrder(int id, [FromBody] CreateOrderDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // Check if order exists (optional but helpful for 404)
        var exists = await _context.Orders.AnyAsync(o => o.Id == id);
        if (!exists)
            return NotFound();

        await _context.Database.ExecuteSqlRawAsync(
            "EXEC usp_UpdateOrder @Id, @ProductName, @Quantity, @Price",
            new SqlParameter("@Id", id),
            new SqlParameter("@ProductName", dto.ProductName),
            new SqlParameter("@Quantity", dto.Quantity),
            new SqlParameter("@Price", dto.Price));

        return NoContent();
    }

    // DELETE: api/orders/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteOrder(int id)
    {
        var exists = await _context.Orders.AnyAsync(o => o.Id == id);
        if (!exists)
            return NotFound();

        await _context.Database.ExecuteSqlRawAsync(
            "EXEC usp_DeleteOrder @Id",
            new SqlParameter("@Id", id));

        return NoContent();
    }
}
Key points:

FromSqlRaw / FromSqlInterpolated are used for SELECT operations.

ExecuteSqlRawAsync is used for INSERT/UPDATE/DELETE with output parameters where needed.

The CreateOrder method uses an OUTPUT parameter to get the new identity, then fetches the full order via usp_GetOrderById.

4. Gateway Configuration – No Changes Needed
The Ocelot gateway configuration (ocelot.json) from the previous step already includes all necessary routes for GET, POST, PUT, and DELETE on /orders. There is no need to modify it because the API contract (URLs, status codes) has not changed.

5. Testing the API with Stored Procedures
The testing steps are exactly the same as before, because the API surface remains identical. Run both projects and use Postman.

Login to obtain a token:

text
POST http://localhost:5000/auth/login
Body: { "username": "admin", "password": "password" }
Create an order (will call usp_CreateOrder):

text
POST http://localhost:5000/orders
Authorization: Bearer <token>
Body: { "productName": "Keyboard", "quantity": 1, "price": 79.99 }
Get all orders (usp_GetOrders):

text
GET http://localhost:5000/orders
Authorization: Bearer <token>
Get order by ID (usp_GetOrderById):

text
GET http://localhost:5000/orders/1
Update order (usp_UpdateOrder):

text
PUT http://localhost:5000/orders/1
Authorization: Bearer <token>
Body: { "productName": "Mechanical Keyboard", "quantity": 2, "price": 99.99 }
Delete order (usp_DeleteOrder):

text
DELETE http://localhost:5000/orders/1
Authorization: Bearer <token>
All operations will now execute through the stored procedures. And it verify this by running a SQL Profiler trace or checking the stored procedure execution log.
