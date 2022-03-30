var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAdB2C");
builder.Services.AddAuthorization();
builder.Services.AddCors(opt => opt.AddPolicy("allowAny", o => o.AllowAnyOrigin()));
builder.Services.AddHealthChecks();

// Configure JSON logging to the console.
builder.Logging.AddJsonConsole();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
  app.UseSwagger();
  app.UseSwaggerUI();
}

app.MapGet("/health", async (HealthCheckService healthCheckService) =>
{
  var report = await healthCheckService.CheckHealthAsync();
  return report.Status == HealthStatus.Healthy ? Results.Ok(report) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).WithTags(new[] { "Health" }).Produces(200).ProducesProblem(503).ProducesProblem(401);

app.MapPost("/createOrder", [Authorize] async (Order order, HttpContext http) =>
{
  const string documentClaim = "extension_Document";
  const string emailClaim = "Emails";  
  var httpUser = http.User;

  MercadoPagoConfig.AccessToken = app.Configuration["MercadoPago:AccessToken"] ?? "";

  var request = new PaymentCreateRequest
  {
    TransactionAmount = order.Price,
    Description = order.ProductName,
    PaymentMethodId = "pix",
    Payer = new PaymentPayerRequest
    {
      Email = httpUser.FindFirst(emailClaim)?.Value,
      FirstName = httpUser.FindFirst(ClaimTypes.GivenName)?.Value,
      LastName = httpUser.FindFirst(ClaimTypes.Surname)?.Value,
      Identification = new IdentificationRequest
      {
        Type = "CPF",
        Number = httpUser.FindFirst(documentClaim)?.Value,
      },
    },
  };

  var client = new PaymentClient();
  try {
    Payment payment = await client.CreateAsync(request);
    var orderResponse = new OrderResponse(order, payment);

    return Results.Created($"/todoitems/{payment.Id}", orderResponse);
  } catch(Exception ex)
  {
      app.Logger.LogError(ex, ex.Message);
      return Results.Problem(ex.Message);
  }

}).WithName("CreateOrder").Produces(201, typeof(OrderResponse)).ProducesProblem(401);

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseCors();

app.Run();

public class Order
{
  public Guid Id { get; set; }
  public Guid ProductId { get; set; }
  public string ProductName { get; set; }
  public Guid UserId { get; set; }
  public decimal Price { get; set; }
  public List<int> Numbers { get; set; }

  public Order()
  {
    Numbers = new List<int>();
    ProductName = String.Empty;
  }
}

public class OrderResponse
{
  public Guid Id { get; set; }
  public string? Base64Img { get; set; }
  public string? PixCopiaCola { get; set; }

  public OrderResponse() { }
  public OrderResponse(Order order, Payment payment) =>
  (Id, Base64Img, PixCopiaCola) = (order.Id, payment.PointOfInteraction.TransactionData.QrCodeBase64, payment.PointOfInteraction.TransactionData.QrCode);
}

public static class F
{
  public static string Dump(object obj)
  {
    return JsonConvert.SerializeObject(obj);
  }
}