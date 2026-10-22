using HarveyOverhaul.Core.Api;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Services;
using HarveyStressMeter.UI;

namespace HarveyStressMeter.Api;

/// <summary>
/// Единая шкала доверия для других модов. Injury начисляет сюда очки за соблюдение режима травм,
/// и во вкладке «Доверие» видна одна картина вместо двух разных «доверий».
/// </summary>
public sealed class StressCareTrustApi : IHarveyCareTrustApi
{
    private readonly HarveyCareTrustService _trustService;

    public StressCareTrustApi(HarveyCareTrustService trustService)
    {
        _trustService = trustService;
    }

    public int Points => _trustService.GetTrustPoints();

    public int Level => _trustService.GetTrustLevel();

    public string LevelTitle => Level switch
    {
        HarveyCareTrustLevels.Anchor or HarveyCareTrustLevels.SafePerson => HarveyPanelTexts.Trust.HighTitle,
        HarveyCareTrustLevels.TrustedDoctor => HarveyPanelTexts.Trust.CautiousTitle,
        _ => HarveyPanelTexts.Trust.ObservingTitle,
    };

    public void Award(string sourceProviderId, string reason, int points)
        => _trustService.AwardTrustFromSource(SourceKey(sourceProviderId), reason, points);

    public void Penalize(string sourceProviderId, string reason, int points)
        => _trustService.PenalizeTrustFromSource(SourceKey(sourceProviderId), reason, points);

    public bool IsConsentAllowed(string consentId)
        => _trustService.IsConsentAllowed(consentId);

    /// <summary>Короткий ключ источника для PointsBySource («injury» вместо полного UniqueID).</summary>
    private static string SourceKey(string providerId)
        => providerId.Contains("Injury", StringComparison.OrdinalIgnoreCase) ? "injury" : providerId;
}
