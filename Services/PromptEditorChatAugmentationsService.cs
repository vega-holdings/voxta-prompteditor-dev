using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Chats.Sessions;
using Voxta.Abstractions.Security;
using Voxta.Abstractions.Services;
using Voxta.Abstractions.Services.ChatAugmentations;
using Voxta.Model.Shared;

namespace Voxta.Modules.PromptEditor.Services;

public class PromptEditorChatAugmentationsService(ILogger<PromptEditorChatAugmentationsService> logger)
    : ServiceBase(logger), IChatAugmentationsService
{
    public Task<IChatAugmentationServiceInstanceBase[]> CreateInstanceAsync(
        IChatSessionChatAugmentationApi session,
        IAuthenticationContext auth,
        CancellationToken cancellationToken)
    {
        if (!session.IsAugmentationEnabled(VoxtaModule.AugmentationKey))
        {
            return Task.FromResult(Array.Empty<IChatAugmentationServiceInstanceBase>());
        }

        IChatAugmentationServiceInstanceBase[] instances = [new PromptEditorAugmentationInstance()];
        return Task.FromResult(instances);
    }
}

file sealed class PromptEditorAugmentationInstance : IChatAugmentationServiceInstanceBase
{
    public ServiceTypes[] GetRequiredServiceTypes() => [];
    public string[] GetAugmentationNames() => [VoxtaModule.AugmentationKey];
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

