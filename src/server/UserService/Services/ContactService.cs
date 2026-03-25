using Microsoft.EntityFrameworkCore;
using UserService.Data;
using UserService.Models;

namespace UserService.Services;

public interface IContactService
{
    Task<Contact?> AddContactAsync(long userId, long contactUserId, string? contactName = null);
    Task<bool> RemoveContactAsync(long userId, long contactUserId);
    Task<List<ContactDto>> GetContactsAsync(long userId);
    Task<Contact?> GetContactAsync(long userId, long contactUserId);
    Task<bool> UpdateContactNameAsync(long userId, long contactUserId, string contactName);
    Task<bool> ToggleFavoriteAsync(long userId, long contactUserId);
}

public class ContactService : IContactService
{
    private readonly UserDbContext _context;
    private readonly ILogger<ContactService> _logger;

    public ContactService(UserDbContext context, ILogger<ContactService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Contact?> AddContactAsync(long userId, long contactUserId, string? contactName = null)
    {
        // Check if already a contact
        var existing = await _context.Contacts
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ContactUserId == contactUserId);
        
        if (existing != null)
        {
            _logger.LogWarning("Contact already exists: {UserId} -> {ContactUserId}", userId, contactUserId);
            return existing;
        }

        // Check if mutual
        var isMutual = await _context.Contacts
            .AnyAsync(c => c.UserId == contactUserId && c.ContactUserId == userId);

        var contact = new Contact
        {
            UserId = userId,
            ContactUserId = contactUserId,
            ContactName = contactName,
            IsMutual = isMutual
        };

        _context.Contacts.Add(contact);

        // Update mutual status for the other contact if it exists
        if (isMutual)
        {
            var otherContact = await _context.Contacts
                .FirstOrDefaultAsync(c => c.UserId == contactUserId && c.ContactUserId == userId);
            if (otherContact != null)
            {
                otherContact.IsMutual = true;
            }
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Added contact: {UserId} -> {ContactUserId}", userId, contactUserId);
        return contact;
    }

    public async Task<bool> RemoveContactAsync(long userId, long contactUserId)
    {
        var contact = await _context.Contacts
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ContactUserId == contactUserId);
        
        if (contact == null) return false;

        _context.Contacts.Remove(contact);

        // Update mutual status for the other contact
        var otherContact = await _context.Contacts
            .FirstOrDefaultAsync(c => c.UserId == contactUserId && c.ContactUserId == userId);
        if (otherContact != null)
        {
            otherContact.IsMutual = false;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Removed contact: {UserId} -> {ContactUserId}", userId, contactUserId);
        return true;
    }

    public async Task<List<ContactDto>> GetContactsAsync(long userId)
    {
        var contacts = await _context.Contacts
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.IsFavorite)
            .ThenBy(c => c.ContactName)
            .ToListAsync();

        var result = new List<ContactDto>();
        foreach (var contact in contacts)
        {
            var profile = await _context.UserProfiles.FindAsync(contact.ContactUserId);
            if (profile != null)
            {
                var avatar = await _context.UserAvatars
                    .FirstOrDefaultAsync(a => a.UserId == contact.ContactUserId && a.IsActive);

                result.Add(new ContactDto
                {
                    Id = contact.Id,
                    ContactUserId = contact.ContactUserId,
                    ContactName = contact.ContactName ?? profile.Username,
                    IsFavorite = contact.IsFavorite,
                    IsMutual = contact.IsMutual,
                    AddedAt = contact.AddedAt,
                    Profile = UserProfileDto.FromProfile(profile, avatar?.FileId, avatar?.SmallFileId)
                });
            }
        }

        return result;
    }

    public async Task<Contact?> GetContactAsync(long userId, long contactUserId)
    {
        return await _context.Contacts
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ContactUserId == contactUserId);
    }

    public async Task<bool> UpdateContactNameAsync(long userId, long contactUserId, string contactName)
    {
        var contact = await _context.Contacts
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ContactUserId == contactUserId);
        
        if (contact == null) return false;

        contact.ContactName = contactName;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Updated contact name: {UserId} -> {ContactUserId}", userId, contactUserId);
        return true;
    }

    public async Task<bool> ToggleFavoriteAsync(long userId, long contactUserId)
    {
        var contact = await _context.Contacts
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ContactUserId == contactUserId);
        
        if (contact == null) return false;

        contact.IsFavorite = !contact.IsFavorite;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Toggled favorite: {UserId} -> {ContactUserId} ({IsFavorite})", 
            userId, contactUserId, contact.IsFavorite);
        return true;
    }
}

public class ContactDto
{
    public long Id { get; set; }
    public long ContactUserId { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public bool IsMutual { get; set; }
    public DateTime AddedAt { get; set; }
    public UserProfileDto? Profile { get; set; }
}
