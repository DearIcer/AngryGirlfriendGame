using AngryGirlfriendGame.Models;
using AngryGirlfriendGame.Services;
using Microsoft.AspNetCore.Mvc;

namespace AngryGirlfriendGame.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly ILogger<ChatController> _logger;
    private readonly OpenAiService _openAIService;

    public ChatController(OpenAiService openAiService, ILogger<ChatController> logger)
    {
        _openAIService = openAiService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] ChatMessage chatMessage)
    {
        try
        {
            if (string.IsNullOrEmpty(chatMessage.Message)) return BadRequest("消息不能为空");

            var response = await _openAIService.GetGirlfriendResponseAsync(
                chatMessage.Message,
                chatMessage.Score,
                chatMessage.Attempts);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理聊天消息时出错");
            return StatusCode(500, "服务器内部错误");
        }
    }
}