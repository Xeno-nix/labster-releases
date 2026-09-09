namespace CLMS.Application.Authentication.Interfaces;

public interface IResetTokenHasher
{
    string Hash(string resetToken);

    bool Verify(string resetToken, string hashToken);
}
