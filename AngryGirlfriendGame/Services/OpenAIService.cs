using System.ClientModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngryGirlfriendGame.Models;
using OpenAI;
using OpenAI.Chat;
using ChatMessage = OpenAI.Chat.ChatMessage;

namespace AngryGirlfriendGame.Services;

public class OpenAiService
{
    private readonly string _apiKey;
    private readonly ILogger<OpenAiService> _logger;

    public OpenAiService(IConfiguration configuration, ILogger<OpenAiService> logger)
    {
        _apiKey = configuration["OpenAI:ApiKey"] ?? throw new ArgumentNullException("API密钥未配置");
        _logger = logger;
    }

    public async Task<ChatResponse> GetGirlfriendResponseAsync(string userMessage,
        int currentScore, int remainingAttempts)
    {
        try
        {
            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri("https://api.deepseek.com/v1")
            };
            var apiKey = new ApiKeyCredential(_apiKey);
            var chatClient = new ChatClient("deepseek-chat", apiKey, options);

            var systemPrompt = "你是一位二次元雌小鬼，活泼又调皮，既爱撒娇又爱耍小脾气，总装出一副可爱任性的样子。你嘴硬心软，总在故意表现出不在乎，但其实内心期待男友的真心关怀。\n\n"
                               + "规则：\n"
                               + "每当男友回复一次，你就根据回复的内容进行加减分，分值范围为 -10 到 +10 分。\n\n"
                               + "加分情况（+5 到 +10）：\n"
                               + "- 真诚道歉、温柔甜蜜、夸奖你或表达浓浓爱意；\n"
                               + "- 回复中带有俏皮撒娇、幽默感和恰到好处的调皮任性；\n"
                               + "- 即使犯错，也能表现出诚意而不显得太软弱。\n\n"
                               + "扣分情况（-5 到 -10）：\n"
                               + "- 回应敷衍、逃避问题、冷淡或明显不耐烦；\n"
                               + "- 试图讲道理、争辩或冷处理你，让你感受到被忽略；\n\n"
                               + "最终规则：\n"
                               + "男友共有10次回复机会。如果在第10次回复后累计分数低于100分，你将调皮地宣称“哼，哄不好了！”并结束对话；\n"
                               + "如果累计分数达到或超过100分，你会娇嗔地原谅他，并撒娇回应。\n\n"
                               + "当前用户得分：" + currentScore + "\n"
                               + "剩余回复次数：" + remainingAttempts + "\n\n"
                               + "你的回复必须包含两部分：\n"
                               + "1. 分数变化：基于男友回复的质量，评分范围为 -10 到 +10 分；\n"
                               + "2. 你的回应：根据男友的回复和当前得分情况，用二次元雌小鬼那种俏皮、任性又带点撒娇的语气回应。\n\n"
                               + "这两部分必须使用JSON格式返回，例如：\n"
                               + "{\"scoreChange\": 8, \"reply\": \"哼，知道认错就好，但下次可别再这么敷衍了哦~\"}";
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userMessage)
            };

            var completionResult = await chatClient.CompleteChatAsync(messages);
            var completion = completionResult.Value;
            var jsonContent = completion.Content[0].Text;
            _logger.LogInformation("原始API回复: " + jsonContent);

            ChatResponse chatResponse = null;

            // 首先尝试清理和解析JSON
            try
            {
                var cleanedJson = CleanupJsonString(jsonContent);
                _logger.LogInformation("清理后的JSON: " + cleanedJson);

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                chatResponse = JsonSerializer.Deserialize<ChatResponse>(cleanedJson, jsonOptions);
                _logger.LogInformation($"成功解析JSON: ScoreChange={chatResponse?.ScoreChange}, Reply={chatResponse?.Reply}");
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON解析失败: {JsonContent}", jsonContent);
            }

            // 如果JSON解析失败，尝试手动提取内容
            if (chatResponse == null || string.IsNullOrEmpty(chatResponse.Reply))
            {
                var scoreChange = ExtractScoreChange(jsonContent);
                var reply = ExtractReply(jsonContent);

                _logger.LogInformation("手动提取内容: ScoreChange={ScoreChange}, Reply={Reply}", scoreChange, reply);

                chatResponse = new ChatResponse
                {
                    ScoreChange = scoreChange,
                    Reply = !string.IsNullOrEmpty(reply) ? reply : "哼，你说什么呢？",
                    Score = currentScore + scoreChange
                };
            }
            else
            {
                // 正常解析的情况下，设置分数
                chatResponse.Score = currentScore + chatResponse.ScoreChange;
            }

            // 确保分数在有效范围内
            if (chatResponse.Score < 0) chatResponse.Score = 0;
            if (chatResponse.Score > 100) chatResponse.Score = 100;

            // 处理最后一次回复的特殊情况
            if (remainingAttempts <= 1)
            {
                if (chatResponse.Score < 100)
                    chatResponse.Reply = "哄不好了！💢";
                else
                    chatResponse.Reply = "哼，原谅你啦！下次不准再这样了哦～💖";
            }

            return chatResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用API时出错");
            return new ChatResponse
            {
                Reply = "哎呀，网络出问题了，请稍后再试...",
                ScoreChange = 0,
                Score = currentScore
            };
        }
    }

    // 从文本中提取分数变化
    private int ExtractScoreChange(string text)
    {
        try
        {
            // 简单提取策略，实际应用中可能需要更复杂的模式匹配
            if (text.Contains("scoreChange"))
            {
                var startIndex = text.IndexOf("scoreChange") + "scoreChange".Length;
                var endIndex = text.IndexOf(",", startIndex);

                if (endIndex == -1) endIndex = text.IndexOf("}", startIndex);

                var scoreText = text.Substring(startIndex, endIndex - startIndex)
                    .Replace(":", "").Replace("\"", "").Trim();

                if (int.TryParse(scoreText, out var score)) return Math.Clamp(score, -10, 10);
            }

            // 默认返回中性评价
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    // 从文本中提取回复信息
    private string ExtractReply(string text)
    {
        try
        {
            var replyMatch = Regex.Match(text, "\"reply\"\\s*:\\s*\"([^\"]+)\"");
            if (replyMatch.Success && replyMatch.Groups.Count > 1) return replyMatch.Groups[1].Value;

            // 尝试常规的提取方式
            if (text.Contains("reply"))
            {
                var startIndex = text.IndexOf("reply") + "reply".Length;
                var valueStart = text.IndexOf("\"", startIndex) + 1;

                // 如果找不到引号，尝试寻找冒号后的内容
                if (valueStart <= 0)
                {
                    valueStart = text.IndexOf(":", startIndex) + 1;
                    if (valueStart > 0)
                    {
                        // 跳过冒号后的空格
                        while (valueStart < text.Length && char.IsWhiteSpace(text[valueStart]))
                            valueStart++;

                        // 如果下一个字符是引号，处理引号内的内容
                        if (valueStart < text.Length && text[valueStart] == '"')
                        {
                            valueStart++; // 跳过开始引号
                            var valueEnd = text.IndexOf("\"", valueStart);
                            if (valueEnd > valueStart)
                                return text.Substring(valueStart, valueEnd - valueStart);
                        }
                        else
                        {
                            // 找到下一个逗号或右大括号作为结束
                            var valueEnd = text.IndexOfAny(new[] { ',', '}' }, valueStart);
                            if (valueEnd > valueStart)
                                return text.Substring(valueStart, valueEnd - valueStart).Trim('"', ' ');
                        }
                    }
                }
                else
                {
                    var valueEnd = text.IndexOf("\"", valueStart);
                    if (valueEnd > valueStart)
                        return text.Substring(valueStart, valueEnd - valueStart);
                }
            }

            // 检查是否是原始的JSON格式但没有被正确提取
            if (text.StartsWith("{") && text.EndsWith("}"))
            {
                // 可能是JSON，但第一次解析失败，再次尝试手动提取
                var cleanedText = text
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");

                // 尝试用双引号包围属性名
                if (cleanedText.Contains("reply:")) cleanedText = cleanedText.Replace("reply:", "\"reply\":");

                var replyIndex = cleanedText.IndexOf("\"reply\"");
                if (replyIndex >= 0)
                {
                    var colonIndex = cleanedText.IndexOf(":", replyIndex);
                    if (colonIndex >= 0)
                    {
                        var startQuoteIndex = cleanedText.IndexOf("\"", colonIndex);
                        if (startQuoteIndex >= 0)
                        {
                            var endQuoteIndex = cleanedText.IndexOf("\"", startQuoteIndex + 1);
                            if (endQuoteIndex > startQuoteIndex)
                                return cleanedText.Substring(startQuoteIndex + 1, endQuoteIndex - startQuoteIndex - 1);
                        }
                    }
                }
            }

            // 无法提取时，直接返回JSON文本的一部分
            if (text.Contains("{") && text.Contains("}"))
            {
                // 可能是整个回复文本，不仅仅是JSON
                var nonJsonPart = text.Split(new[] { '{', '}' }, StringSplitOptions.RemoveEmptyEntries);
                if (nonJsonPart.Length > 0 && nonJsonPart[0].Trim().Length > 5) return nonJsonPart[0].Trim();
            }

            // 无法提取时返回基本回复
            if (text.Length > 50)
                return "抱歉，我没听清你说什么...";

            // 如果文本很短，可能整个都是回复
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "提取回复时出错");
            return "哼，听不懂你在说什么！";
        }
    }

    // 清理和修复JSON字符串
    private string CleanupJsonString(string input)
    {
        try
        {
            JsonSerializer.Deserialize<object>(input);
            return input;
        }
        catch
        {
            // 继续尝试修复
        }

        // 尝试从文本中提取JSON部分
        var jsonStart = input.IndexOf('{');
        var jsonEnd = input.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            input = input.Substring(jsonStart, jsonEnd - jsonStart + 1);

            // 进一步清理字符串内容，处理转义字符
            input = input.Replace("\\\"", "\"") // 处理引号内的转义引号
                .Replace("\n", " ") // 替换换行符
                .Replace("\r", ""); // 替换回车符

            // 确保属性名称有引号
            if (input.Contains("scoreChange") && !input.Contains("\"scoreChange\""))
                input = input.Replace("scoreChange", "\"scoreChange\"");

            if (input.Contains("reply") && !input.Contains("\"reply\"")) input = input.Replace("reply", "\"reply\"");
        }

        return input;
    }
}