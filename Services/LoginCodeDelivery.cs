using System.Threading.Channels;

namespace Loom.Services;

public sealed class LoginCodeDelivery(
    Channel<LoginCodeDelivery.Message> queue,
    LoginCodes codes,
    StoreEmail mail,
    ILogger<LoginCodeDelivery> logger) : BackgroundService
{
    public sealed record Message(string ChallengeKey, string Email, string Code);

    public bool Enqueue(Message message) => queue.Writer.TryWrite(message);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (!codes.Exists(message.ChallengeKey))
                    continue;
                if (!await mail.SendLoginCodeAsync(message.Email, message.Code, stoppingToken))
                {
                    codes.Fail(message.ChallengeKey);
                    logger.LogWarning("Could not deliver a Loom login code.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                codes.Fail(message.ChallengeKey);
                logger.LogError(exception, "Unexpected login code delivery error.");
            }
        }
    }
}
