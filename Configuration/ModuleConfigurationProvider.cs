using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Registration;
using Voxta.Abstractions.Security;
using Voxta.Model.ApiMessages.Requests;
using Voxta.Model.ApiMessages.Responses;
using Voxta.Model.Shared.Forms;
using Voxta.Modules.PromptEditor.Services;

namespace Voxta.Modules.PromptEditor.Configuration;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global", Justification = "Field names are reused in module registration.")]
public class ModuleConfigurationProvider(
    PromptEditorStore store,
    ILogger<ModuleConfigurationProvider> logger
) : ModuleConfigurationProviderBase, IModuleConfigurationProvider
{
    public const string CollectionName = "CollectionName";
    public const string NewCollectionName = "NewCollectionName";
    public const string Language = "Language";

    public const string ActionCreateCollection = "ActionCreateCollection";
    public const string ActionApplyCollection = "ActionApplyCollection";
    public const string ActionRestoreOriginal = "ActionRestoreOriginal";

    public static string[] FieldsRequiringReload => [];

    public Task<FormField[]> GetModuleConfigurationFieldsAsync(
        IAuthenticationContext auth,
        ISettingsSource settings,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(auth.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(FormBuilder.Build(
                FormTitleField.Create("Prompt Editor", "Admin-only.", false)
            ));
        }

        store.EnsureDataFolders();

        var languages = store.ListLanguages();
        var selectedLanguage = settings.GetRawValue(Language).Trim();
        if (string.IsNullOrWhiteSpace(selectedLanguage) || !languages.Contains(selectedLanguage, StringComparer.OrdinalIgnoreCase))
        {
            selectedLanguage = languages.FirstOrDefault(x => string.Equals(x, "en", StringComparison.OrdinalIgnoreCase)) ?? languages.FirstOrDefault() ?? "en";
        }

        var collections = store.ListCollections();
        var selectedCollection = settings.GetRawValue(CollectionName).Trim();
        if (!string.IsNullOrWhiteSpace(selectedCollection) && !collections.Contains(selectedCollection, StringComparer.OrdinalIgnoreCase))
        {
            selectedCollection = collections.FirstOrDefault() ?? string.Empty;
        }

        var status = new List<string>
        {
            "Open the editor: /manage/prompt-editor (or click Help on the module card).",
            $"Live root: {store.LiveRoot}",
            $"Data root: {store.DataRoot}",
            $"Originals: {store.OriginalsRoot}",
            $"Collections: {store.CollectionsRoot}",
            $"Language: {selectedLanguage}",
            $"Selected collection: {(string.IsNullOrWhiteSpace(selectedCollection) ? "(none)" : selectedCollection)}",
            "Note: action buttons use saved values only — Save first, then click.",
        };

        var languageField = new FormChoicesField
        {
            Name = Language,
            Label = "Language",
            Text = "Language folder under Resources/Prompts/Default.",
            Choices = languages.Select(x => new FormChoice { Label = x, Value = x }).ToArray(),
            DefaultValue = selectedLanguage,
        };

        var collectionField = new FormChoicesField
        {
            Name = CollectionName,
            Label = "Collection",
            Text = "Used for apply operations (selected language).",
            Choices = (collections.Count == 0)
                ? [new FormChoice { Label = "(none)", Value = "" }]
                : collections.Select(x => new FormChoice { Label = x, Value = x }).ToArray(),
            DefaultValue = collections.Count == 0 ? "" : collections[0],
        };

        var newCollectionField = new FormTextField
        {
            Name = NewCollectionName,
            Label = "New Collection Name",
            Text = "Type a name, Save, then click “Create Collection from Live”.",
            Placeholder = "my-prompts",
        };

        return Task.FromResult(FormBuilder.Build(
            FormTitleField.Create(
                "Prompt Editor",
                "Use the Help button on the module card (or open /manage/prompt-editor). This config page is only for quick collection operations.",
                false),
            FormDocumentationField.Create(string.Join(Environment.NewLine, status), "Status"),
            languageField,
            collectionField,
            newCollectionField,
            new FormInvokeActionField { Name = ActionCreateCollection, Label = "", ButtonText = "Create Collection from Live (selected language)" },
            new FormInvokeActionField { Name = ActionApplyCollection, Label = "", ButtonText = "Apply Collection to Live (selected language)" },
            new FormInvokeActionField { Name = ActionRestoreOriginal, Label = "", ButtonText = "Restore Originals to Live (selected language)" }
        ));
    }

    public override Task<FormInvokeActionResponse> InvokeAction(
        IAuthenticationContext auth,
        StaticSettingsSource settings,
        FormInvokeActionRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(auth.Role, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new FormInvokeActionResponse { Text = "Admin-only." });
        }

        try
        {
            var lang = settings.GetRawValue(Language).Trim();
            if (string.IsNullOrWhiteSpace(lang))
            {
                lang = "en";
            }

            if (request.FieldName == ActionCreateCollection)
            {
                var name = settings.GetRawValue(NewCollectionName).Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Task.FromResult(new FormInvokeActionResponse { Text = "Set New Collection Name, Save, then click again." });
                }

                var created = store.CreateCollectionFromLive(name, lang);
                return Task.FromResult(new FormInvokeActionResponse { Text = $"Created collection '{created}' for '{lang}'." });
            }

            if (request.FieldName == ActionApplyCollection)
            {
                var selectedCollection = settings.GetRawValue(CollectionName).Trim();
                if (string.IsNullOrWhiteSpace(selectedCollection))
                {
                    return Task.FromResult(new FormInvokeActionResponse { Text = "Select a Collection, Save, then click again." });
                }

                store.ApplyCollectionToLive(selectedCollection, lang);
                return Task.FromResult(new FormInvokeActionResponse { Text = $"Applied collection '{selectedCollection}' to Live for '{lang}'." });
            }

            if (request.FieldName == ActionRestoreOriginal)
            {
                store.RestoreOriginalsToLive(lang);
                return Task.FromResult(new FormInvokeActionResponse { Text = $"Restored Originals to Live for '{lang}'." });
            }

            return Task.FromResult(new FormInvokeActionResponse { Text = "No action." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PromptEditor InvokeAction failed");
            return Task.FromResult(new FormInvokeActionResponse { Text = $"Error: {ex.Message}" });
        }
    }
}

