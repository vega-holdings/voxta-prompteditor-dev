using JetBrains.Annotations;
using Voxta.Abstractions.Modules;
using Voxta.Abstractions.Registration;
using Voxta.Model.Shared;
using Voxta.Modules.PromptEditor.Configuration;
using Voxta.Modules.PromptEditor.Services;

namespace Voxta.Modules.PromptEditor;

[UsedImplicitly]
public class VoxtaModule : IVoxtaModule
{
    public const string ServiceName = "PromptEditor";
    public const string AugmentationKey = "promptEditor";

    public void Configure(IVoxtaModuleBuilder builder)
    {
        builder.Register(new ModuleDefinition
        {
            ServiceName = ServiceName,
            Label = "Prompt Editor (Core System) [alpha]",
            Notes = "Admin-only editor for system Scriban prompts under Resources/Prompts/Default, with an originals backup and swappable prompt collections.",
            HelpLink = "https://doc.voxta.ai/",
            Experimental = true,
            Single = true,
            CanBeInstalledByAdminsOnly = true,
            Supports = new()
            {
                { ServiceTypes.ChatAugmentations, ServiceDefinitionCategoryScore.Low },
            },
            Pricing = ServiceDefinitionPricing.Free,
            Hosting = ServiceDefinitionHosting.Builtin,
            SupportsExplicitContent = true,
            Recommended = false,
            Augmentations = [AugmentationKey],
            ModuleConfigurationProviderType = typeof(ModuleConfigurationProvider),
            ModuleConfigurationFieldsRequiringReload = ModuleConfigurationProvider.FieldsRequiringReload,
        });

        builder.AddChatAugmentationsService<PromptEditorChatAugmentationsService>(ServiceName);
    }
}

