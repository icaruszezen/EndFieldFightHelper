namespace EndFieldFightHelper.Models;

/// <summary>
/// YOLO 模型输出的类别名称常量。修改模型类别名时只需更新此处。
/// </summary>
public static class YoloLabels
{
    public const string ActiveCharacter = "当前角色";
    public const string HealthBar = "血条";
    public const string UltimateCharge = "终结技充能";
    public const string DodgePrompt = "闪避提示";
    public const string ChainTrigger = "连携触发";
    public const string SkillCharged = "技力充能完成";
}
