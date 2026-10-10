using System.Reflection;

namespace LiveCue.Relay.Tests.Contracts;

/// <summary>
/// I-07: no event record has a member named <c>token</c>, <c>authorization</c>, <c>payloadText</c> or
/// <c>nonce</c>, in any letter case. The check follows the records an event holds, such as the status
/// object of a PC model event.
/// </summary>
public sealed class EventShapeTests
{
    private static readonly string[] Forbidden = ["token", "authorization", "payloadText", "nonce"];

    [Fact]
    public void No_event_carries_a_secret_member()
    {
        var events = typeof(RelayEvent).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(RelayEvent)) && !type.IsAbstract)
            .ToList();

        Assert.Equal(17, events.Count);
        foreach (var type in events)
        {
            foreach (var name in MemberNames(type, []))
            {
                Assert.DoesNotContain(name, Forbidden, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void The_pairing_matrix_object_has_no_token_member_either()
    {
        Assert.Equal(["Endpoint", "Modules"], MemberNames(typeof(PairingQr), []).Order());
    }

    [Fact]
    public void Every_event_is_a_sealed_record_with_its_time()
    {
        var events = typeof(RelayEvent).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(RelayEvent)));

        Assert.All(events, type =>
        {
            Assert.True(type.IsSealed, type.Name + " is not sealed.");
            Assert.True(type.IsPublic, type.Name + " is not public.");
            Assert.Equal(typeof(DateTimeOffset), type.GetProperty(nameof(RelayEvent.At))!.PropertyType);
        });
    }

    private static IEnumerable<string> MemberNames(Type type, HashSet<Type> seen)
    {
        if (!seen.Add(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (property.Name == "EqualityContract")
            {
                continue;
            }

            yield return property.Name;

            // Follow the relay's own records; a string, a number or an enum holds no further member.
            var inner = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (inner.Assembly == typeof(RelayEvent).Assembly && !inner.IsEnum)
            {
                foreach (var name in MemberNames(inner, seen))
                {
                    yield return name;
                }
            }
        }
    }
}
