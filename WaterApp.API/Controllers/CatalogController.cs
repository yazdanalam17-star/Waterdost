using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;

namespace WaterApp.API.Controllers;

// Public storefront browsing — no auth required to look around,
// but posting a review requires a signed-in buyer.
[ApiController]
[Route("api")]
[AllowAnonymous]
public class CatalogController : ControllerBase
{
    private readonly IBuyerService _buyerService;

    public CatalogController(IBuyerService buyerService)
    {
        _buyerService = buyerService;
    }

    [HttpGet("sellers")]
    public async Task<ActionResult<List<SellerDto>>> GetSellersInArea([FromQuery] string pincode, [FromQuery] string? category = null)
    {
        try
        {
            return Ok(await _buyerService.GetSellersInAreaAsync(pincode, category));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("products")]
    public async Task<ActionResult<List<ProductWithSellerDto>>> GetProductsByCategory([FromQuery] string pincode, [FromQuery] string category)
    {
        try
        {
            return Ok(await _buyerService.GetProductsByCategoryAsync(pincode, category));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("sellers/{id}/products")]
    public async Task<ActionResult<List<ProductDto>>> GetSellerProducts(Guid id)
    {
        try
        {
            return Ok(await _buyerService.GetSellerProductsAsync(id));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // Public — buyers browsing without an account need to see product
    // photos too. Sellers upload via POST /api/seller/products/{id}/image.
    [HttpGet("products/{id}/image")]
    public async Task<IActionResult> GetProductImage(Guid id)
    {
        var image = await _buyerService.GetProductImageAsync(id);
        if (image is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(image.Value.Data, image.Value.ContentType);
    }

    [HttpGet("sellers/{id}/reviews")]
    public async Task<ActionResult<List<SellerReviewDto>>> GetSellerReviews(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            return Ok(await _buyerService.GetSellerReviewsAsync(id, page, pageSize));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("sellers/{id}/reviews")]
    [Authorize(Roles = "Buyer")]
    public async Task<ActionResult<SellerReviewDto>> AddSellerReview(Guid id, CreateReviewRequest request)
    {
        var buyerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        try
        {
            return Ok(await _buyerService.AddReviewAsync(buyerId, id, request));
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
}
