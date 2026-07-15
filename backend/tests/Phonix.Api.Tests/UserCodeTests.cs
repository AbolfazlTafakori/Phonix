using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Account codes are Telegram-style numeric ids: random 6-digit to start, growing a digit when the current
// tier is saturated. These pin the format, the uniqueness guarantee, and the tier escalation.
public class UserCodeTests
{
    [Fact]
    public void A_new_registration_gets_a_random_six_digit_numeric_code()
    {
        var store = TestStore.Create();
        var user = store.RegisterUser(new AppUser { Username = "codeuser", Password = "x" });

        Assert.Equal(6, user.Code.Length);
        Assert.All(user.Code, c => Assert.True(char.IsAsciiDigit(c)));
        Assert.NotEqual('0', user.Code[0]); // no leading zero — it's a number, not a padded string
    }

    [Fact]
    public void Codes_do_not_collide_across_many_registrations()
    {
        var store = TestStore.Create();
        var codes = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var u = store.RegisterUser(new AppUser { Username = $"bulk{i}", Password = "x" });
            Assert.True(codes.Add(u.Code), $"duplicate code {u.Code}");
        }
    }

    [Fact]
    public void A_saturated_tier_escalates_to_more_digits_like_telegram()
    {
        // Pretend every 6- and 7-digit code is taken: the generator must come back with an 8-digit one
        // instead of looping forever or throwing.
        var code = UserCodes.Next(candidate => candidate.Length < 8);
        Assert.Equal(8, code.Length);
    }
}
