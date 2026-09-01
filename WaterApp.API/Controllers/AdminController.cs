using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;

namespace WaterApp.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    [HttpGet("stats")]
    public async Task<ActionResult<AdminStatsResponse>> GetStats()
    {
        var stats = await _adminService.GetStatsAsync();
        return Ok(stats);
    }

    [HttpGet("sellers")]
    public async Task<ActionResult<List<AdminSellerResponse>>> GetSellers(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            var sellers = await _adminService.GetSellersAsync(status, page, pageSize);
            return Ok(sellers);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("sellers/{id}/status")]
    public async Task<ActionResult<AdminSellerResponse>> UpdateSellerStatus(Guid id, UpdateSellerStatusRequest request)
    {
        try
        {
            var seller = await _adminService.UpdateSellerStatusAsync(id, request.Status);
            return Ok(seller);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("buyers")]
    public async Task<ActionResult<List<AdminBuyerResponse>>> GetBuyers(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var buyers = await _adminService.GetBuyersAsync(search, page, pageSize);
        return Ok(buyers);
    }

    [HttpPatch("buyers/{id}/status")]
    public async Task<ActionResult<AdminBuyerResponse>> UpdateBuyerStatus(Guid id, UpdateBuyerStatusRequest request)
    {
        try
        {
            var buyer = await _adminService.UpdateBuyerStatusAsync(id, request.IsActive);
            return Ok(buyer);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
