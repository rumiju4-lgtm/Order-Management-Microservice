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