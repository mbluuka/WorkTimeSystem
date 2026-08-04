using Microsoft.AspNetCore.Mvc;

namespace WorkTime.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            service = "WorkTime.Server"
        });
    }
}