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
/// Unit tests for GroupManagementService
/// </summary>
public class GroupManagementServiceTests : IDisposable
{
    private readonly GroupDbContext _context;
    private readonly Mock<ILogger<GroupManagementService>> _loggerMock;
    private readonly GroupManagementService _service;

    public GroupManagementServiceTests()
    {
        var options = new DbContextOptionsBuilder<GroupDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _context = new GroupDbContext(options);
        _loggerMock = new Mock<ILogger<GroupManagementService>>();
        _service = new GroupManagementService(_context, _loggerMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region CreateGroupAsync Tests

    [Fact]
    public async Task CreateGroupAsync_WithValidRequest_ShouldCreateGroup()
    {
        // Arrange
        var creatorId = Guid.NewGuid();
        var request = new CreateGroupRequest
        {
            Name = "Test Group",
            Description = "A test group",
            Type = GroupType.Public,
            MaxMembers = 100
        };

        // Act
        var result = await _service.CreateGroupAsync(request, creatorId);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be(request.Name);
        result.Description.Should().Be(request.Description);
        result.Type.Should().Be(request.Type);
        result.MaxMembers.Should().Be(request.MaxMembers);
        result.OwnerId.Should().Be(creatorId);
        result.InviteLink.Should().NotBeNullOrEmpty();
        
        // Verify creator was added as member with Creator role
        var member = await _context.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == result.Id && m.UserId == creatorId);
        member.Should().NotBeNull();
        member!.Role.Should().Be(GroupRole.Creator);
    }

    [Fact]
    public async Task CreateGroupAsync_WithInitialMembers_ShouldAddAllMembers()
    {
        // Arrange
        var creatorId = Guid.NewGuid();
        var member1Id = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var request = new CreateGroupRequest
        {
            Name = "Test Group",
            InitialMemberIds = new List<Guid> { member1Id, member2Id, creatorId } // Include creator to test deduplication
        };

        // Act
        var result = await _service.CreateGroupAsync(request, creatorId);

        // Assert
        var members = await _context.GroupMembers.Where(m => m.GroupId == result.Id).ToListAsync();
        members.Should().HaveCount(3); // Creator + 2 initial members
        members.Should().Contain(m => m.UserId == creatorId && m.Role == GroupRole.Creator);
        members.Should().Contain(m => m.UserId == member1Id && m.Role == GroupRole.Member);
        members.Should().Contain(m => m.UserId == member2Id && m.Role == GroupRole.Member);
    }

    #endregion

    #region GetGroupAsync Tests

    [Fact]
    public async Task GetGroupAsync_WithExistingGroupId_ShouldReturnGroup()
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
        var result = await _service.GetGroupAsync(group.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(group.Id);
        result.Name.Should().Be(group.Name);
    }

    [Fact]
    public async Task GetGroupAsync_WithNonExistingGroupId_ShouldReturnNull()
    {
        // Act
        var result = await _service.GetGroupAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetGroupByInviteLinkAsync Tests

    [Fact]
    public async Task GetGroupByInviteLinkAsync_WithValidLink_ShouldReturnGroup()
    {
        // Arrange
        var inviteLink = "unique-invite-link";
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = inviteLink
        };
        _context.Groups.Add(group);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetGroupByInviteLinkAsync(inviteLink);

        // Assert
        result.Should().NotBeNull();
        result!.InviteLink.Should().Be(inviteLink);
    }

    #endregion

    #region UpdateGroupAsync Tests

    [Fact]
    public async Task UpdateGroupAsync_AsAdmin_ShouldUpdateGroup()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Original Name",
            OwnerId = Guid.NewGuid(),
            InviteLink = "test-link"
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

        var request = new UpdateGroupRequest
        {
            Name = "Updated Name",
            Description = "New Description"
        };

        // Act
        var result = await _service.UpdateGroupAsync(group.Id, request, adminId);

        // Assert
        result.Name.Should().Be("Updated Name");
        result.Description.Should().Be("New Description");
    }

    [Fact]
    public async Task UpdateGroupAsync_AsRegularMember_ShouldThrowUnauthorized()
    {
        // Arrange
        var memberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Original Name",
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

        var request = new UpdateGroupRequest { Name = "Updated Name" };

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.UpdateGroupAsync(group.Id, request, memberId));
    }

    #endregion

    #region DeleteGroupAsync Tests

    [Fact]
    public async Task DeleteGroupAsync_AsCreator_ShouldDeleteGroup()
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
        _context.Groups.Add(group);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.DeleteGroupAsync(group.Id, creatorId);

        // Assert
        result.Should().BeTrue();
        var deletedGroup = await _context.Groups.FindAsync(group.Id);
        deletedGroup.Should().BeNull();
    }

    [Fact]
    public async Task DeleteGroupAsync_AsNonCreator_ShouldThrowUnauthorized()
    {
        // Arrange
        var nonCreatorId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(), // Different owner
            InviteLink = "test-link"
        };
        _context.Groups.Add(group);
        await _context.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.DeleteGroupAsync(group.Id, nonCreatorId));
    }

    #endregion

    #region GetUserGroupsAsync Tests

    [Fact]
    public async Task GetUserGroupsAsync_ShouldReturnAllUserGroups()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var group1 = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Group 1",
            OwnerId = userId,
            InviteLink = "link1"
        };
        var group2 = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Group 2",
            OwnerId = Guid.NewGuid(),
            InviteLink = "link2"
        };
        var member1 = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group1.Id,
            UserId = userId,
            Role = GroupRole.Creator,
            IsActive = true
        };
        var member2 = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group2.Id,
            UserId = userId,
            Role = GroupRole.Member,
            IsActive = true
        };
        _context.Groups.AddRange(group1, group2);
        _context.GroupMembers.AddRange(member1, member2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetUserGroupsAsync(userId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(g => g.Name == "Group 1");
        result.Should().Contain(g => g.Name == "Group 2");
    }

    [Fact]
    public async Task GetUserGroupsAsync_ShouldNotIncludeLeftGroups()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Left Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "link"
        };
        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = userId,
            Role = GroupRole.Member,
            IsActive = false // Left group
        };
        _context.Groups.Add(group);
        _context.GroupMembers.Add(member);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetUserGroupsAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GenerateInviteLinkAsync Tests

    [Fact]
    public async Task GenerateInviteLinkAsync_AsAdmin_ShouldGenerateNewLink()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "old-link"
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

        // Act
        var newLink = await _service.GenerateInviteLinkAsync(group.Id, adminId);

        // Assert
        newLink.Should().NotBe("old-link");
        newLink.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateInviteLinkAsync_WhenMembersCanInvite_ShouldAllowMembers()
    {
        // Arrange
        var memberId = Guid.NewGuid();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            OwnerId = Guid.NewGuid(),
            InviteLink = "old-link",
            AllowMembersToInvite = true
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
        var newLink = await _service.GenerateInviteLinkAsync(group.Id, memberId);

        // Assert
        newLink.Should().NotBeNullOrEmpty();
    }

    #endregion
}
