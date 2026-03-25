using System.Collections.Generic;
using System.Threading.Tasks;
using EndFieldFightHelper.Models;

namespace EndFieldFightHelper.Services;

public interface IDialogService
{
    Task<(int DelayMs, bool SuppressDuringSkill)?> ShowDodgeSettingsAsync(int currentDelay, bool currentSuppress);
    Task<string?> ShowAutoSkillOrderAsync(string currentOrder);
    Task<OverlayContentSettings?> ShowOverlayContentSettingsAsync(
        OverlayContentSettings current, IReadOnlyList<string> pipelineNames);
}
