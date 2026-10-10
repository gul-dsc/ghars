using System.Globalization;
using Microsoft.AspNetCore.Identity;

namespace GharsPlatform.Helpers;

/// <summary>
/// Identity's own error messages (password rules, duplicate email, expired reset link) in the
/// language of the current request. Used everywhere Identity reports an error: Admin > Users,
/// password reset and change. The rules themselves are configured in Program.cs and are unchanged.
/// </summary>
public sealed class BilingualIdentityErrorDescriber : IdentityErrorDescriber
{
    private static bool IsAr => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    private static IdentityError E(string code, string en, string ar) => new() { Code = code, Description = IsAr ? ar : en };

    public override IdentityError PasswordTooShort(int length)
        => E(nameof(PasswordTooShort), $"The password must be at least {length} characters long.", $"يجب ألا تقل كلمة المرور عن {length} أحرف.");

    public override IdentityError PasswordRequiresNonAlphanumeric()
        => E(nameof(PasswordRequiresNonAlphanumeric), "The password must include at least one symbol (for example ! @ # $).", "يجب أن تتضمن كلمة المرور رمزاً واحداً على الأقل (مثل ! @ # $).");

    public override IdentityError PasswordRequiresDigit()
        => E(nameof(PasswordRequiresDigit), "The password must include at least one number (0–9).", "يجب أن تتضمن كلمة المرور رقماً واحداً على الأقل (0–9).");

    public override IdentityError PasswordRequiresLower()
        => E(nameof(PasswordRequiresLower), "The password must include at least one lowercase letter (a–z).", "يجب أن تتضمن كلمة المرور حرفاً إنجليزياً صغيراً واحداً على الأقل (a–z).");

    public override IdentityError PasswordRequiresUpper()
        => E(nameof(PasswordRequiresUpper), "The password must include at least one uppercase letter (A–Z).", "يجب أن تتضمن كلمة المرور حرفاً إنجليزياً كبيراً واحداً على الأقل (A–Z).");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars)
        => E(nameof(PasswordRequiresUniqueChars), $"The password must use at least {uniqueChars} different characters.", $"يجب أن تحتوي كلمة المرور على {uniqueChars} أحرف مختلفة على الأقل.");

    public override IdentityError PasswordMismatch()
        => E(nameof(PasswordMismatch), "The password is incorrect.", "كلمة المرور غير صحيحة.");

    public override IdentityError DuplicateEmail(string email)
        => E(nameof(DuplicateEmail), $"An account with the email {email} already exists.", $"يوجد حساب مسجّل بالبريد الإلكتروني {email}.");

    public override IdentityError DuplicateUserName(string userName)
        => E(nameof(DuplicateUserName), $"The username {userName} is already in use.", $"اسم المستخدم {userName} مستخدم بالفعل.");

    public override IdentityError InvalidEmail(string? email)
        => E(nameof(InvalidEmail), $"The email address {email} is not valid.", $"عنوان البريد الإلكتروني {email} غير صالح.");

    public override IdentityError InvalidUserName(string? userName)
        => E(nameof(InvalidUserName), $"The username {userName} is not valid. Use letters, numbers and @ . - _ only.", $"اسم المستخدم {userName} غير صالح. استخدموا الحروف والأرقام والرموز ‎@ . - _‎ فقط.");

    public override IdentityError InvalidToken()
        => E(nameof(InvalidToken), "This link has expired or has already been used. Request a new one.", "انتهت صلاحية هذا الرابط أو سبق استخدامه. اطلبوا رابطاً جديداً.");

    public override IdentityError ConcurrencyFailure()
        => E(nameof(ConcurrencyFailure), "This account was changed by someone else a moment ago. Reload the page and try again.", "عُدّل هذا الحساب قبل لحظات من قبل مستخدم آخر. أعيدوا تحميل الصفحة وحاولوا مرة أخرى.");

    public override IdentityError UserAlreadyInRole(string role)
        => E(nameof(UserAlreadyInRole), $"The user already has the role {role}.", $"المستخدم يحمل الدور {role} بالفعل.");

    public override IdentityError UserNotInRole(string role)
        => E(nameof(UserNotInRole), $"The user does not have the role {role}.", $"المستخدم لا يحمل الدور {role}.");

    public override IdentityError DefaultError()
        => E(nameof(DefaultError), "The change could not be saved. Please try again.", "تعذّر حفظ التغيير. يرجى المحاولة مرة أخرى.");
}
