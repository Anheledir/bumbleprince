﻿using BaseBotService.Core.Base;
using BaseBotService.Infrastructure;
using Serilog;

namespace BaseBotService.Tests.Infrastructure;

public class AchievementFactoryTests
{
    private Faker _faker;
    private IServiceProvider _serviceProvider;

    [SetUp]
    public void SetUp()
    {
        _faker = new Faker();
        _serviceProvider = Substitute.For<IServiceProvider>();
        _serviceProvider.GetService(typeof(CustomHCAchievement)).Returns(new CustomHCAchievement());
        _serviceProvider.GetService(typeof(ILogger)).Returns(Substitute.For<ILogger>());
        Program.ServiceProvider = _serviceProvider;
    }

    [Test]
    public void CreateAchievement_ShouldCreateValidAchievement()
    {
        // Arrange
        ulong userId = _faker.Random.ULong();
        ulong guildId = _faker.Random.ULong();

        // Act
        var achievement = AchievementFactory.CreateAchievement<CustomHCAchievement>(userId, guildId);

        // Assert
        achievement.ShouldNotBeNull();
        achievement.MemberId.ShouldBe(userId);
        achievement.GuildId.ShouldBe(guildId);
        achievement.CreatedAt.ShouldBeLessThanOrEqualTo(SystemClock.Instance.GetCurrentInstant());
    }

    [Test]
    public void CreateAchievement_WithNoGuildId_ShouldCreateValidGlobalAchievement()
    {
        // Arrange
        ulong userId = _faker.Random.ULong();

        // Act
        var achievement = AchievementFactory.CreateAchievement<CustomHCAchievement>(userId, null);

        // Assert
        achievement.ShouldNotBeNull();
        achievement.MemberId.ShouldBe(userId);
        achievement.GuildId.ShouldBeNull();
        achievement.CreatedAt.ShouldBeLessThanOrEqualTo(SystemClock.Instance.GetCurrentInstant());
    }
}

public class CustomHCAchievement : HCAchievementBase
{
    // You can add custom properties or methods for this test class
}
