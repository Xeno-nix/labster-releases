using CLMS.Shared.Dtos;

namespace CLMS.Web.Models;

public sealed record TwoFactorConfirmationModel(TwoFactorSettingReceipt? Receipt, string? Error = null, string? Message = null);
