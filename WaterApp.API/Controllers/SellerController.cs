using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;

namespace WaterApp.API.Controllers;

[ApiController]
[Route("api/seller")]
[Authorize(Roles = "Seller")]
public class SellerController : ControllerBase
{
    private readonly ISellerService _sellerService;

    public SellerController(ISellerService sellerService)
    {
        _sellerService = sellerService;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ---- Profile ----

    [HttpGet("me")]
    public async Task<ActionResult<SellerProfileDto?>> GetMyProfile()
    {
        var profile = await _sellerService.GetMyProfileAsync(CurrentUserId);
        return Ok(profile);
    }

    [HttpPost("register")]
    public async Task<ActionResult<SellerProfileDto>> Register(SellerRegisterRequest request)
    {
        try
        {
            var profile = await _sellerService.RegisterAsync(CurrentUserId, request);
            return Ok(profile);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("payment-settings")]
    public async Task<ActionResult<SellerProfileDto>> UpdatePaymentSettings(UpdatePaymentSettingsRequest request)
    {
        try
        {
            var profile = await _sellerService.UpdatePaymentSettingsAsync(CurrentUserId, request.UpiId);
            return Ok(profile);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ---- Products ----

    [HttpGet("products")]
    public async Task<ActionResult<List<ProductDto>>> GetMyProducts()
    {
        try
        {
            return Ok(await _sellerService.GetMyProductsAsync(CurrentUserId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("products")]
    public async Task<ActionResult<ProductDto>> CreateProduct(ProductCreateRequest request)
    {
        try
        {
            var product = await _sellerService.CreateProductAsync(CurrentUserId, request);
            return Ok(product);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("products/{id}")]
    public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, ProductUpdateRequest request)
    {
        try
        {
            var product = await _sellerService.UpdateProductAsync(CurrentUserId, id, request);
            return Ok(product);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("products/{id}")]
    public async Task<IActionResult> DeleteProduct(Guid id)
    {
        try
        {
            await _sellerService.DeleteProductAsync(CurrentUserId, id);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // ---- Orders ----

    [HttpGet("orders")]
    public async Task<ActionResult<List<SellerOrderDto>>> GetMyOrders([FromQuery] string? status)
    {
        try
        {
            return Ok(await _sellerService.GetMyOrdersAsync(CurrentUserId, status));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("orders/{id}/status")]
    public async Task<ActionResult<SellerOrderDto>> UpdateOrderStatus(Guid id, UpdateOrderStatusRequest request)
    {
        try
        {
            var order = await _sellerService.UpdateOrderStatusAsync(CurrentUserId, id, request.Status);
            return Ok(order);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("orders/{id}/confirm-payment")]
    public async Task<ActionResult<SellerOrderDto>> ConfirmPayment(Guid id)
    {
        try
        {
            var order = await _sellerService.ConfirmPaymentAsync(CurrentUserId, id);
            return Ok(order);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ---- Dashboard ----

    [HttpGet("stats")]
    public async Task<ActionResult<SellerDashboardStatsDto>> GetStats()
    {
        try
        {
            return Ok(await _sellerService.GetDashboardStatsAsync(CurrentUserId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
