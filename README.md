1. Prerequisites
.NET 8 SDK

SQL Server (localDB, Express, or a full instance)

Visual Studio 2022 (recommended) or VS Code / CLI

Postman or any HTTP client for testing

2. Create the Solution and Projects
Open a terminal (or use Visual Studio) and run:

bash
mkdir OrderManagement
cd OrderManagement
dotnet new sln -n OrderManagement

dotnet new webapi -n OrderApi --framework net8.0
dotnet new web -n ApiGateway --framework net8.0

dotnet sln add OrderApi/OrderApi.csproj
dotnet sln add ApiGateway/ApiGateway.csproj
Add the required NuGet packages to OrderApi:

bash
cd OrderApi
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Tools
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
cd ..
Add NuGet packages to ApiGateway:

bash
cd ApiGateway
dotnet add package Ocelot
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
cd ..
3. OrderApi – Downstream Service
3.1 Project Structure
text
OrderApi/
├── Controllers/
│   ├── AuthController.cs
│   └── OrdersController.cs
├── Data/
│   └── OrderDbContext.cs
├── Models/
│   └── Order.cs
├── Dtos/
│   ├── LoginDto.cs
│   └── CreateOrderDto.cs
├── Program.cs
├── appsettings.json
└── ...
3.2 Models & DTOs
Models/Order.cs

csharp
namespace OrderApi.Models;

public class Order
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
}
Dtos/LoginDto.cs

csharp
namespace OrderApi.Dtos;

public class LoginDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
Dtos/CreateOrderDto.cs

csharp
namespace OrderApi.Dtos;

public class CreateOrderDto
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
3.3 Database Context
Data/OrderDbContext.cs

csharp
using Microsoft.EntityFrameworkCore;
using OrderApi.Models;

namespace OrderApi.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
}
3.4 appsettings.json
Replace the existing content with:

json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=OrderDb;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Key": "YourSuperSecretKeyThatIsAtLeast32CharactersLong!",
    "Issuer": "OrderApi",
    "Audience": "OrderApiClient"
  }
}
Adjust the connection string to your SQL Server instance if necessary.

3.5 Program.cs – OrderApi
csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrderApi.Data;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Auto-migrate database on startup (for simplicity)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    db.Database.EnsureCreated();
}

app.Run();
3.6 Controllers
Controllers/AuthController.cs

csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OrderApi.Dtos;

namespace OrderApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginDto loginDto)
    {
        // Dummy user validation (hardcoded for demo)
        if (loginDto.Username != "admin" || loginDto.Password != "password")
            return Unauthorized("Invalid credentials");

        var jwtSettings = _configuration.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, loginDto.Username),
            new Claim(ClaimTypes.Role, "User")
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );

        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
    }
}
Controllers/OrdersController.cs

csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderApi.Data;
using OrderApi.Dtos;
using OrderApi.Models;

namespace OrderApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize] // All endpoints require authentication
public class OrdersController : ControllerBase
{
    private readonly OrderDbContext _context;

    public OrdersController(OrderDbContext context)
    {
        _context = context;
    }

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

        return CreatedAtAction(nameof(CreateOrder), new { id = order.Id }, order);
    }
}
3.7 Configure Ports for OrderApi
Open Properties/launchSettings.json inside OrderApi and set the application URL to http://localhost:5001:

json
{
  "profiles": {
    "OrderApi": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5001",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
4. ApiGateway – Ocelot Gateway
4.1 ocelot.json
Create a file named ocelot.json in the root of the ApiGateway project:

json
{
  "Routes": [
    {
      "DownstreamPathTemplate": "/api/orders",
      "DownstreamScheme": "http",
      "DownstreamHostAndPorts": [
        {
          "Host": "localhost",
          "Port": 5001
        }
      ],
      "UpstreamPathTemplate": "/orders",
      "UpstreamHttpMethod": [ "Post" ],
      "AuthenticationOptions": {
        "AuthenticationProviderKey": "Bearer"
      }
    },
    {
      "DownstreamPathTemplate": "/api/auth/login",
      "DownstreamScheme": "http",
      "DownstreamHostAndPorts": [
        {
          "Host": "localhost",
          "Port": 5001
        }
      ],
      "UpstreamPathTemplate": "/auth/login",
      "UpstreamHttpMethod": [ "Post" ]
    }
  ],
  "GlobalConfiguration": {
    "BaseUrl": "http://localhost:5000"
  }
}
Explanation

POST /orders → Gateway validates JWT, then forwards to OrderApi (port 5001).

POST /auth/login → Gateway forwards without authentication (public endpoint).

The gateway itself will run on http://localhost:5000.

4.2 Program.cs – ApiGateway
csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Load ocelot.json
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

// Add Ocelot services
builder.Services.AddOcelot();

// JWT Authentication (same secret/issuer/audience as OrderApi)
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer("Bearer", options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Ocelot middleware
await app.UseOcelot();

app.Run();
4.3 appsettings.json – ApiGateway
Add the same JWT section to appsettings.json:

json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Jwt": {
    "Key": "YourSuperSecretKeyThatIsAtLeast32CharactersLong!",
    "Issuer": "OrderApi",
    "Audience": "OrderApiClient"
  }
}
4.4 Launch Settings for ApiGateway
Modify Properties/launchSettings.json:

json
{
  "profiles": {
    "ApiGateway": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5000",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
5. Database Setup
Because we used EnsureCreated() in OrderApi/Program.cs, the database and Orders table are created automatically when the application first starts. No manual SQL script is required.

If you prefer to run migrations manually, do the following (inside the OrderApi folder):

bash
dotnet ef migrations add InitialCreate
dotnet ef database update
Make sure your SQL Server is running and the connection string is correct.

6. Running and Testing the API
6.1 Start Both Projects
Set both OrderApi and ApiGateway as startup projects in Visual Studio, or run them separately from the terminal:

bash
# Terminal 1 – OrderApi
cd OrderApi
dotnet run

# Terminal 2 – ApiGateway
cd ApiGateway
dotnet run
You should see:

OrderApi listening on http://localhost:5001

ApiGateway listening on http://localhost:5000

6.2 Test with Postman (or cURL)
Step 1 – Obtain a JWT token
Request

text
POST http://localhost:5000/auth/login
Content-Type: application/json
{
    "username": "admin",
    "password": "password"
}
Expected Response (200 OK)

json
{
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
Copy the token value.

Step 2 – Create an Order
Request

text
POST http://localhost:5000/orders
Content-Type: application/json
Authorization: Bearer {your_token}
{
    "productName": "Laptop",
    "quantity": 1,
    "price": 1200.00
}
Expected Response (201 Created)

json
{
    "id": 1,
    "productName": "Laptop",
    "quantity": 1,
    "price": 1200.00,
    "orderDate": "2026-06-02T..."
}
6.3 Verify the Database
Open SQL Server Management Studio (or any client) and query the Orders table in the OrderDb database. You should see the newly created order.

6.4 Test Without Token (Should Fail)
Send the same POST /orders request without the Authorization header, or with an invalid token. The gateway will return 401 Unauthorized.

7. Architecture Recap
text
                HTTP Request
                      │
                      ▼
        ┌─────────────────────────┐
        │   ApiGateway (:5000)    │
        │   - Ocelot routing      │
        │   - JWT validation      │
        └─────────────────────────┘
                      │
                      ▼
        ┌─────────────────────────┐
        │   OrderApi (:5001)      │
        │   - JWT validation      │
        │   - Business logic      │
        │   - SQL Server DB       │
        └─────────────────────────┘
The gateway validates the JWT token before forwarding the request.

The downstream service also validates it (defence in depth).

Unauthenticated requests never reach the microservice.
