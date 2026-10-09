namespace GharsPlatform.ViewModels;

/// <summary>
/// Everything the public certificate verification page may show, and nothing more. Built in the
/// controller from the certificate row so the page cannot reach the participant, the revocation
/// reason, who revoked it, or where the PDF is stored. The revocation reason is for DSC
/// administrators only and appears on Admin/Certificates.
/// </summary>
public sealed class CertificateVerificationVm
{
    public required string CertificateNo { get; init; }

    public required bool IsValid { get; init; }

    public required DateTime IssuedAtUtc { get; init; }

    public string? ActivityTitleEn { get; init; }

    public string? ActivityTitleAr { get; init; }
}
