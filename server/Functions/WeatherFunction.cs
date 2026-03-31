using System.Text.Json;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AchieveAi.LmDotnetTools.LmCore.Models;

namespace AIChat.Server.Functions;

/// <summary>
/// Example weather function for testing tool calls
/// </summary>
public class WeatherFunction : IFunctionProvider
{
    private readonly ILogger<WeatherFunction> _logger;

    public WeatherFunction(ILogger<WeatherFunction> logger)
    {
        _logger = logger;
    }
    
    public string ProviderName => "WeatherAPI";
    public int Priority => 100;

    public IEnumerable<FunctionDescriptor> GetFunctions()
    {
        yield return new FunctionDescriptor
        {
            Contract = new FunctionContract
            {
                Name = "get_weather",
                Description = "Get the current weather for a city",
                Parameters = new[]
                {
                    new FunctionParameterContract
                    {
                        Name = "city",
                        ParameterType = JsonSchemaObject.String("The city to get weather for"),
                        Description = "The city to get weather for",
                        IsRequired = true
                    }
                }
            },
            Handler = GetWeatherAsync,
            ProviderName = "WeatherAPI"
        };

        yield return new FunctionDescriptor
        {
            Contract = new FunctionContract
            {
                Name = "calculate",
                Description = "Perform a mathematical calculation",
                Parameters = new[]
                {
                    new FunctionParameterContract
                    {
                        Name = "expression",
                        ParameterType = JsonSchemaObject.String("Mathematical expression to evaluate (e.g., '2+2', '10*5')"),
                        Description = "Mathematical expression to evaluate (e.g., '2+2', '10*5')",
                        IsRequired = true
                    }
                }
            },
            Handler = CalculateAsync,
            ProviderName = "Calculator"
        };
    }

    private async Task<string> GetWeatherAsync(string argsJson)
    {
        _logger.LogInformation("GetWeather called with args: {Args}", argsJson);
        
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);
            var city = args?["city"].GetString() ?? "Unknown";
            
            // Simulate API delay
            await Task.Delay(500);
            
            // Return mock weather data
            var weather = new
            {
                city = city,
                temperature = Random.Shared.Next(10, 35),
                condition = new[] { "Sunny", "Cloudy", "Rainy", "Partly Cloudy" }[Random.Shared.Next(0, 4)],
                humidity = Random.Shared.Next(30, 80),
                wind_speed = Random.Shared.Next(5, 25)
            };
            
            return JsonSerializer.Serialize(weather);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetWeather function");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private async Task<string> CalculateAsync(string argsJson)
    {
        _logger.LogInformation("Calculate called with args: {Args}", argsJson);
        
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);
            var expression = args?["expression"].GetString() ?? "0";
            
            // Simple evaluation (in production, use a proper expression evaluator)
            var result = expression switch
            {
                var e when e.Contains('+') => EvaluateSimple(e, '+'),
                var e when e.Contains('-') => EvaluateSimple(e, '-'),
                var e when e.Contains('*') => EvaluateSimple(e, '*'),
                var e when e.Contains('/') => EvaluateSimple(e, '/'),
                _ => 0
            };
            
            await Task.Delay(200); // Simulate processing
            
            return JsonSerializer.Serialize(new { result = result, expression = expression });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Calculate function");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private double EvaluateSimple(string expression, char op)
    {
        var parts = expression.Split(op);
        if (parts.Length != 2) return 0;
        
        if (double.TryParse(parts[0].Trim(), out var a) && 
            double.TryParse(parts[1].Trim(), out var b))
        {
            return op switch
            {
                '+' => a + b,
                '-' => a - b,
                '*' => a * b,
                '/' => b != 0 ? a / b : 0,
                _ => 0
            };
        }
        return 0;
    }
}