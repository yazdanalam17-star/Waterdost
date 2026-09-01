using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;

namespace WaterApp.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize(Roles = "Buyer")]
public class OrdersController : ControllerBase
{
    private readonly IBuyerService _buyerService;

    public OrdersController(IBuyerService buyerService)
    {
        _buyerService = buyerService;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<ActionResult<OrderDto>> PlaceOrder(PlaceOrderRequest request)
    {
        try
        {
            return Ok(await _buyerService.PlaceOrderAsync(CurrentUserId, request));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("me")]
    public async Task<ActionResult<List<OrderDto>>> GetMyOrders(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            return Ok(await _buyerService.GetMyOrdersAsync(CurrentUserId, status, page, pageSize));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<BuyerOrderDetailDto>> GetOrderDetail(Guid id)
    {
        try
        {
            return Ok(await _buyerService.GetOrderDetailAsync(CurrentUserId, id));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<OrderDto>> CancelOrder(Guid id)
    {
        try
        {
            return Ok(await _buyerService.CancelOrderAsync(CurrentUserId, id));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
