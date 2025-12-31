using System.Text.Json.Serialization;

namespace Voxta.Modules.PromptEditor.Models;

/// <summary>
/// SillyTavern preset format for prompt injection
/// </summary>
public sealed class SillyTavernPreset
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 1.0;

    [JsonPropertyName("frequency_penalty")]
    public double FrequencyPenalty { get; set; }

    [JsonPropertyName("presence_penalty")]
    public double PresencePenalty { get; set; }

    [JsonPropertyName("top_p")]
    public double TopP { get; set; } = 1.0;

    [JsonPropertyName("top_k")]
    public int TopK { get; set; }

    [JsonPropertyName("top_a")]
    public double TopA { get; set; }

    [JsonPropertyName("min_p")]
    public double MinP { get; set; }

    [JsonPropertyName("repetition_penalty")]
    public double RepetitionPenalty { get; set; } = 1.0;

    [JsonPropertyName("openai_max_context")]
    public string OpenAiMaxContext { get; set; } = "";

    [JsonPropertyName("openai_max_tokens")]
    public int OpenAiMaxTokens { get; set; } = 2048;

    [JsonPropertyName("wrap_in_quotes")]
    public bool WrapInQuotes { get; set; }

    [JsonPropertyName("names_behavior")]
    public int NamesBehavior { get; set; }

    [JsonPropertyName("send_if_empty")]
    public string SendIfEmpty { get; set; } = "";

    [JsonPropertyName("impersonation_prompt")]
    public string ImpersonationPrompt { get; set; } = "";

    [JsonPropertyName("new_chat_prompt")]
    public string NewChatPrompt { get; set; } = "";

    [JsonPropertyName("new_group_chat_prompt")]
    public string NewGroupChatPrompt { get; set; } = "";

    [JsonPropertyName("new_example_chat_prompt")]
    public string NewExampleChatPrompt { get; set; } = "";

    [JsonPropertyName("continue_nudge_prompt")]
    public string ContinueNudgePrompt { get; set; } = "";

    [JsonPropertyName("bias_preset_selected")]
    public string BiasPresetSelected { get; set; } = "";

    [JsonPropertyName("max_context_unlocked")]
    public bool MaxContextUnlocked { get; set; }

    [JsonPropertyName("wi_format")]
    public string WiFormat { get; set; } = "";

    [JsonPropertyName("scenario_format")]
    public string ScenarioFormat { get; set; } = "";

    [JsonPropertyName("personality_format")]
    public string PersonalityFormat { get; set; } = "";

    [JsonPropertyName("group_nudge_prompt")]
    public string GroupNudgePrompt { get; set; } = "";

    [JsonPropertyName("stream_openai")]
    public bool StreamOpenAi { get; set; } = true;

    [JsonPropertyName("prompts")]
    public List<PromptEntry> Prompts { get; set; } = [];

    [JsonPropertyName("prompt_order")]
    public List<PromptOrderGroup> PromptOrder { get; set; } = [];

    [JsonPropertyName("assistant_prefill")]
    public string AssistantPrefill { get; set; } = "";

    [JsonPropertyName("assistant_impersonation")]
    public string AssistantImpersonation { get; set; } = "";

    [JsonPropertyName("claude_use_sysprompt")]
    public bool ClaudeUseSysprompt { get; set; } = true;

    [JsonPropertyName("use_makersuite_sysprompt")]
    public bool UseMakersuiteSysprompt { get; set; }

    [JsonPropertyName("squash_system_messages")]
    public bool SquashSystemMessages { get; set; }

    [JsonPropertyName("image_inlining")]
    public bool ImageInlining { get; set; }

    [JsonPropertyName("inline_image_quality")]
    public string InlineImageQuality { get; set; } = "";

    [JsonPropertyName("video_inlining")]
    public bool VideoInlining { get; set; }

    [JsonPropertyName("continue_prefill")]
    public bool ContinuePrefill { get; set; }

    [JsonPropertyName("continue_postfix")]
    public string ContinuePostfix { get; set; } = "";

    [JsonPropertyName("function_calling")]
    public bool FunctionCalling { get; set; }

    [JsonPropertyName("show_thoughts")]
    public bool ShowThoughts { get; set; }

    [JsonPropertyName("reasoning_effort")]
    public string ReasoningEffort { get; set; } = "";

    [JsonPropertyName("enable_web_search")]
    public bool EnableWebSearch { get; set; }

    [JsonPropertyName("request_images")]
    public bool RequestImages { get; set; }

    [JsonPropertyName("seed")]
    public int Seed { get; set; } = -1;

    [JsonPropertyName("n")]
    public int N { get; set; } = 1;

    [JsonExtensionData]
    public Dictionary<string, object>? Extensions { get; set; }
}

/// <summary>
/// Individual prompt entry in a preset
/// </summary>
public sealed class PromptEntry
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("system_prompt")]
    public bool SystemPrompt { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("marker")]
    public bool Marker { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = "system";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("injection_position")]
    public int InjectionPosition { get; set; }

    [JsonPropertyName("injection_depth")]
    public int InjectionDepth { get; set; }

    [JsonPropertyName("injection_order")]
    public int InjectionOrder { get; set; }

    [JsonPropertyName("injection_trigger")]
    public List<string>? InjectionTrigger { get; set; }

    [JsonPropertyName("forbid_overrides")]
    public bool ForbidOverrides { get; set; }
}

/// <summary>
/// Prompt order entry
/// </summary>
public sealed class PromptOrderEntry
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

/// <summary>
/// Prompt order group (grouped by character_id)
/// </summary>
public sealed class PromptOrderGroup
{
    [JsonPropertyName("character_id")]
    public int CharacterId { get; set; }

    [JsonPropertyName("order")]
    public List<PromptOrderEntry> Order { get; set; } = [];
}
