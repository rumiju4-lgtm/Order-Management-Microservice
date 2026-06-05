using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderApi.Data;
using OrderApi.Dtos;
using OrderApi.Models;

namespace OrderApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize] // All endpoints require authentication
public class OrdersBkController : ControllerBase
{
    private readonly OrderDbContext _context;

    public OrdersBkController(OrderDbContext context)
    {
        _context = context;
    }

    // [HttpPost]
    // public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto dto)
    // {
    //     if (!ModelState.IsValid)
    //         return BadRequest(ModelState);

    //     var order = new Order
    //     {
    //         ProductName = dto.ProductName,
    //         Quantity = dto.Quantity,
    //         Price = dto.Price,
    //         OrderDate = DateTime.UtcNow
    //     };

    //     _context.Orders.Add(order);
    //     await _context.SaveChangesAsync();

    //     return CreatedAtAction(nameof(CreateOrder), new { id = order.Id }, order);
    // }
    
    // GET: api/orders
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
    {
        return await _context.Orders.ToListAsync();
    }

    // GET: api/orders/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<Order>> GetOrder(int id)
    {
        var order = await _context.Orders.FindAsync(id);

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

        var order = new Order
        {
            ProductName = dto.ProductName,
            Quantity = dto.Quantity,
            Price = dto.Price,
            OrderDate = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
    }

    // PUT: api/orders/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateOrder(int id, [FromBody] CreateOrderDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();

        order.ProductName = dto.ProductName;
        order.Quantity = dto.Quantity;
        order.Price = dto.Price;
        // OrderDate typically remains unchanged; if you want to update it, uncomment the next line
        // order.OrderDate = DateTime.UtcNow;

        _context.Entry(order).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        return NoContent(); // 204 – standard for successful updates
    }

    // DELETE: api/orders/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteOrder(int id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();

        _context.Orders.Remove(order);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}