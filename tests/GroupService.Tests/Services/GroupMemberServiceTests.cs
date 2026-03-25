using Microsoft.EntityFrameworkCore;
using Moq;
using FluentAssertions;
using Xunit;
using GroupService.Data;
using GroupService.Entities;
using GroupService.Models;
using GroupService.Services;

namespace GroupService.Tests.Services;

/// <summary>
/// Unit tests for GroupMemberService
/// </summary>
public class GroupMemberServiceTests : IDisposable
{
    private readonly GroupDbContext _context;
    private readonly Mock<ILogger<GroupMemberService>> _loggerMock;
    private readonly GroupMemberService _service;

    public GroupMemberServiceTests()
    {
        var options = new DbContextOptionsBuilder<GroupDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _context = new GroupDbContext(options);
        _loggerMock = new Mock<ILogger<GroupMemberService>>();
        _service = new GroupMemberService(_context, _loggerMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region AddMemberAsync Tests

    [Fact]
    public async Task AddMemberAsync_WithValidRequest_ShouldAddMember()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var newMemberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = adminId,
            InviteLink = "test-link",
            MaxMembers = 100
        };
        var adminMember = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = adminId,
            Role = GroupRole.Admin,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.Add(adminMember);
        await _context.SaveChangesAsync();

        var request = new AddMemberRequest
        {
            UserId = newMemberId,
            Role = GroupRole.Member
        };

        // Act
        var result = await _service.AddMemberAsync(group.Id, request, adminId);

        // Assert
        result.Should().NotBeNull();
        result.UserId.Should().Be(newMemberId);
        result.Role.Should().Be(GroupRole.Member);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AddMemberAsync_ToFullGroup_ShouldThrowException()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var newMemberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = adminId,
            InviteLink = "test-link",
            MaxMembers = 1
        };
        var adminMember = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = adminId,
            Role = GroupRole.Admin,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.Add(adminMember);
        await _context.SaveChangesAsync();

        var request = new AddMemberRequest { UserId = newMemberId };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AddMemberAsync(group.Id, request, adminId));
    }

    [Fact]
    public async Task AddMemberAsync_ExistingMember_ShouldReactivate()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var existingMemberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = adminId,
            InviteLink = "test-link",
            MaxMembers = 100
        };
        var adminMember = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = adminId,
            Role = GroupRole.Admin,
            IsActive = true
        };
        var existingMember = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = existingMemberId,
            Role = GroupRole.Member,
            IsActive = false, // Left the group
            LeftAt = DateTime.UtcNow.AddDays(-1)
        };
        _context.Groups.Add(group);
        _context.GroupMembers.AddRange(adminMember, existingMember);
        await _context.SaveChangesAsync();

        var request = new AddMemberRequest
        {
            UserId = existingMemberId,
            Role = GroupRole.Moderator
        };

        // Act
        var result = await _service.AddMemberAsync(group.Id, request, adminId);

        // Assert
        result.IsActive.Should().BeTrue();
        result.Role.Should().Be(GroupRole.Moderator);
        result.LeftAt.Should().BeNull();
    }

    #endregion

    #region RemoveMemberAsync Tests

    [Fact]
    public async Task RemoveMemberAsync_AsModerator_ShouldRemoveMember()
    {
        // Arrange
        var moderatorId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
        };
        var moderator = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = moderatorId,
            Role = GroupRole.Moderator,
            IsActive = true
        };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = memberId,
            Role = GroupRole.Member,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.AddRange(moderator, member);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.RemoveMemberAsync(group.Id, memberId, moderatorId);

        // Assert
        result.Should().BeTrue();
        var removedMember = await _context.GroupMembers.FindAsync(member.Id);
        removedMember!.IsActive.Should().BeFalse();
        removedMember.LeftAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveMemberAsync_CannotRemoveHigherRole_ShouldThrowUnauthorized()
    {
        // Arrange
        var moderatorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
        };
        var moderator = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = moderatorId,
            Role = GroupRole.Moderator,
            IsActive = true
        };
        var admin = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = adminId,
            Role = GroupRole.Admin,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.AddRange(moderator, admin);
        await _context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.RemoveMemberAsync(group.Id, adminId, moderatorId));
    }

    [Fact]
    public async Task RemoveMemberAsync_CannotRemoveCreator_ShouldThrowUnauthorized()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = creatorId,
            InviteLink = "test-link"
        };
        var admin = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = adminId,
            Role = GroupRole.Admin,
            IsActive = true
        };
        var creator = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = creatorId,
            Role = GroupRole.Creator,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.AddRange(admin, creator);
        await _context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.RemoveMemberAsync(group.Id, creatorId, adminId));
    }

    #endregion

    #region LeaveGroupAsync Tests

    [Fact]
    public async Task LeaveGroupAsync_AsMember_ShouldSucceed()
    {
        // Arrange
        var memberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
        };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = memberId,
            Role = GroupRole.Member,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.Add(member);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.LeaveGroupAsync(group.Id, memberId);

        // Assert
        result.Should().BeTrue();
        var leftMember = await _context.GroupMembers.FindAsync(member.Id);
        leftMember!.IsActive.Should().BeFalse();
        leftMember.LeftAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LeaveGroupAsync_AsCreator_ShouldThrowException()
    {
        // Arrange
        var creatorId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = creatorId,
            InviteLink = "test-link"
        };
        var creator = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = creatorId,
            Role = GroupRole.Creator,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.Add(creator);
        await _context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.LeaveGroupAsync(group.Id, creatorId));
    }

    #endregion

    #region UpdateMemberRoleAsync Tests

    [Fact]
    public async Task UpdateMemberRoleAsync_AsAdmin_ShouldUpdateRole()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
        };
        var admin = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = adminId,
            Role = GroupRole.Admin,
            IsActive = true
        };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = memberId,
            Role = GroupRole.Member,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.AddRange(admin, member);
        await _context.SaveChangesAsync();

        var request = new UpdateMemberRoleRequest
        {
            Role = GroupRole.Moderator,
            CustomTitle = "Super Mod"
        };

        // Act
        var result = await _service.UpdateMemberRoleAsync(group.Id, memberId, request, adminId);

        // Assert
        result.Role.Should().Be(GroupRole.Moderator);
        result.CustomTitle.Should().Be("Super Mod");
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_CannotAssignHigherRole_ShouldThrowUnauthorized()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
        };
        var admin = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = adminId,
            Role = GroupRole.Admin,
            IsActive = true
        };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = memberId,
            Role = GroupRole.Member,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.AddRange(admin, member);
        await _context.SaveChangesAsync();

        var request = new UpdateMemberRoleRequest { Role = GroupRole.Admin };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.UpdateMemberRoleAsync(group.Id, memberId, request, adminId));
    }

    #endregion

    #region MuteMemberAsync Tests

    [Fact]
    public async Task MuteMemberAsync_AsModerator_ShouldMuteMember()
    {
        // Arrange
        var moderatorId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
        };
        var moderator = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = moderatorId,
            Role = GroupRole.Moderator,
            IsActive = true
        };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = memberId,
            Role = GroupRole.Member,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.AddRange(moderator, member);
        await _context.SaveChangesAsync();

        var duration = TimeSpan.FromHours(1);

        // Act
        var result = await _service.MuteMemberAsync(group.Id, memberId, moderatorId, duration);

        // Assert
        result.Should().BeTrue();
        var mutedMember = await _context.GroupMembers.FindAsync(member.Id);
        mutedMember!.IsMuted.Should().BeTrue();
        mutedMember.MutedUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task UnmuteMemberAsync_ShouldUnmuteMember()
    {
        // Arrange
        var moderatorId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
        };
        var moderator = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = moderatorId,
            Role = GroupRole.Moderator,
            IsActive = true
        };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = memberId,
            Role = GroupRole.Member,
            IsActive = true,
            IsMuted = true,
            MutedUntil = DateTime.UtcNow.AddHours(1)
        };
        _context.Groups.Add(group);
        _context.GroupMembers.AddRange(moderator, member);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.UnmuteMemberAsync(group.Id, memberId, moderatorId);

        // Assert
        result.Should().BeTrue();
        var unmutedMember = await _context.GroupMembers.FindAsync(member.Id);
        unmutedMember!.IsMuted.Should().BeFalse();
        unmutedMember.MutedUntil.Should().BeNull();
    }

    #endregion

    #region IsMemberAsync Tests

    [Fact]
    public async Task IsMemberAsync_WhenActiveMember_ShouldReturnTrue()
    {
        // Arrange
        var memberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
        };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = memberId,
            Role = GroupRole.Member,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.Add(member);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.IsMemberAsync(group.Id, memberId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsMemberAsync_WhenNotMember_ShouldReturnFalse()
    {
        // Arrange
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
        };
        _context.Groups.Add(group);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.IsMemberAsync(group.Id, Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region HasPermissionAsync Tests

    [Theory]
    [InlineData(GroupRole.Creator, GroupRole.Admin, true)]
    [InlineData(GroupRole.Creator, GroupRole.Moderator, true)]
    [InlineData(GroupRole.Creator, GroupRole.Member, true)]
    [InlineData(GroupRole.Admin, GroupRole.Moderator, true)]
    [InlineData(GroupRole.Admin, GroupRole.Member, true)]
    [InlineData(GroupRole.Moderator, GroupRole.Member, true)]
    [InlineData(GroupRole.Moderator, GroupRole.Admin, false)]
    [InlineData(GroupRole.Member, GroupRole.Moderator, false)]
    public async Task HasPermissionAsync_ShouldReturnCorrectResult(
        GroupRole userRole, GroupRole requiredRole, bool expectedResult)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
        };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = userId,
            Role = userRole,
            IsActive = true
        };
        _context.Groups.Add(group);
        _context.GroupMembers.Add(member);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.HasPermissionAsync(group.Id, userId, requiredRole);

        // Assert
        result.Should().Be(expectedResult);
    }

    #endregion
}
