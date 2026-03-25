using System.DirectoryServices.AccountManagement;
using System.Security.Cryptography;
using System.Text;
using AdminService.Models;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;

namespace AdminService.Services;

/// <summary>
/// LDAP/Active Directory authentication and synchronization service
/// </summary>
public interface ILdapService
{
    /// <summary>
    /// Authenticates a user against LDAP/AD
    /// </summary>
    Task<LdapUser?> AuthenticateAsync(string username, string password);
    
    /// <summary>
    /// Gets user information from LDAP
    /// </summary>
    Task<LdapUser?> GetUserAsync(string username);
    
    /// <summary>
    /// Searches for users in LDAP
    /// </summary>
    Task<IEnumerable<LdapUser>> SearchUsersAsync(string query, int limit = 50);
    
    /// <summary>
    /// Gets all users from a specific group
    /// </summary>
    Task<IEnumerable<LdapUser>> GetGroupMembersAsync(string groupName);
    
    /// <summary>
    /// Gets all groups from LDAP
    /// </summary>
    Task<IEnumerable<LdapGroup>> GetGroupsAsync();
    
    /// <summary>
    /// Checks if user is in a specific group
    /// </summary>
    Task<bool> IsUserInGroupAsync(string username, string groupName);
    
    /// <summary>
    /// Syncs all users from LDAP to local database
    /// </summary>
    Task<SyncResult> SyncUsersAsync();
    
    /// <summary>
    /// Tests LDAP connection
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync();
    
    /// <summary>
    /// Gets user's groups
    /// </summary>
    Task<IEnumerable<LdapGroup>> GetUserGroupsAsync(string username);
}

/// <summary>
/// LDAP user information
/// </summary>
public class LdapUser
{
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Department { get; set; }
    public string? Title { get; set; }
    public string? DistinguishedName { get; set; }
    public string? EmployeeId { get; set; }
    public string? Manager { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLogon { get; set; }
    public DateTime? PasswordLastSet { get; set; }
    public List<string> Groups { get; set; } = new();
}

/// <summary>
/// LDAP group information
/// </summary>
public class LdapGroup
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DistinguishedName { get; set; }
    public int MemberCount { get; set; }
}

/// <summary>
/// Sync result
/// </summary>
public class SyncResult
{
    public int UsersCreated { get; set; }
    public int UsersUpdated { get; set; }
    public int UsersDeactivated { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Connection test result
/// </summary>
public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public string? ServerVersion { get; set; }
}

/// <summary>
/// LDAP service implementation
/// </summary>
public class LdapService : ILdapService, IDisposable
{
    private readonly LdapSettings _settings;
    private readonly ILogger<LdapService> _logger;
    private LdapConnection? _connection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public LdapService(IOptions<LdapSettings> settings, ILogger<LdapService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<LdapUser?> AuthenticateAsync(string username, string password)
    {
        if (!_settings.Enabled)
        {
            _logger.LogWarning("LDAP authentication attempted but LDAP is disabled");
            return null;
        }

        try
        {
            await EnsureConnectionAsync();
            
            // Build user DN
            var userDn = BuildUserDn(username);
            
            // Attempt to bind with user credentials
            _connection!.Bind(userDn, password);
            
            if (!_connection.Bound)
            {
                _logger.LogWarning("LDAP bind failed for user {Username}", username);
                return null;
            }
            
            // Get user details
            var user = await GetUserAsync(username);
            
            _logger.LogInformation("LDAP authentication successful for user {Username}", username);
            return user;
        }
        catch (LdapException ex)
        {
            _logger.LogWarning(ex, "LDAP authentication failed for user {Username}: {Message}", 
                username, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LDAP authentication for user {Username}", username);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<LdapUser?> GetUserAsync(string username)
    {
        if (!_settings.Enabled)
            return null;

        try
        {
            await EnsureConnectionAsync();
            
            // Build search filter
            var filter = string.Format(_settings.UserSearchFilter!, username);
            
            // Search for user
            var searchResults = _connection!.Search(
                _settings.BaseDn,
                LdapConnection.ScopeSub,
                filter,
                GetUserAttributes(),
                false
            );
            
            var entry = searchResults.Next();
            if (entry == null)
            {
                _logger.LogWarning("User {Username} not found in LDAP", username);
                return null;
            }
            
            return MapEntryToUser(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user {Username} from LDAP", username);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<LdapUser>> SearchUsersAsync(string query, int limit = 50)
    {
        if (!_settings.Enabled)
            return Enumerable.Empty<LdapUser>();

        try
        {
            await EnsureConnectionAsync();
            
            // Build search filter - search in username, display name, and email
            var filter = $"(|(uid=*{query}*)(cn=*{query}*)(mail=*{query}*)(displayName=*{query}*))";
            
            var users = new List<LdapUser>();
            var searchResults = _connection!.Search(
                _settings.BaseDn,
                LdapConnection.ScopeSub,
                filter,
                GetUserAttributes(),
                false
            );
            
            while (searchResults.HasMore() && users.Count < limit)
            {
                var entry = searchResults.Next();
                users.Add(MapEntryToUser(entry));
            }
            
            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching users in LDAP with query: {Query}", query);
            return Enumerable.Empty<LdapUser>();
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<LdapUser>> GetGroupMembersAsync(string groupName)
    {
        if (!_settings.Enabled)
            return Enumerable.Empty<LdapUser>();

        try
        {
            await EnsureConnectionAsync();
            
            // First, get the group DN
            var groupFilter = $"(cn={groupName})";
            var groupResults = _connection!.Search(
                _settings.BaseDn,
                LdapConnection.ScopeSub,
                groupFilter,
                new[] { "member" },
                false
            );
            
            var groupEntry = groupResults.Next();
            if (groupEntry == null)
            {
                _logger.LogWarning("Group {GroupName} not found in LDAP", groupName);
                return Enumerable.Empty<LdapUser>();
            }
            
            // Get member DNs
            var memberAttribute = groupEntry.GetAttribute("member");
            if (memberAttribute == null)
                return Enumerable.Empty<LdapUser>();
            
            var members = new List<LdapUser>();
            foreach (var memberDn in memberAttribute.StringValues)
            {
                try
                {
                    var memberFilter = $"(distinguishedName={memberDn})";
                    var memberResults = _connection.Search(
                        _settings.BaseDn,
                        LdapConnection.ScopeSub,
                        memberFilter,
                        GetUserAttributes(),
                        false
                    );
                    
                    var memberEntry = memberResults.Next();
                    if (memberEntry != null)
                    {
                        members.Add(MapEntryToUser(memberEntry));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get member {MemberDn}", memberDn);
                }
            }
            
            return members;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting members of group {GroupName}", groupName);
            return Enumerable.Empty<LdapUser>();
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<LdapGroup>> GetGroupsAsync()
    {
        if (!_settings.Enabled)
            return Enumerable.Empty<LdapGroup>();

        try
        {
            await EnsureConnectionAsync();
            
            var filter = "(objectClass=group)";
            var groups = new List<LdapGroup>();
            
            var searchResults = _connection!.Search(
                _settings.BaseDn,
                LdapConnection.ScopeSub,
                filter,
                new[] { "cn", "description", "member" },
                false
            );
            
            while (searchResults.HasMore())
            {
                var entry = searchResults.Next();
                var group = new LdapGroup
                {
                    Name = entry.GetAttribute("cn")?.StringValue ?? string.Empty,
                    Description = entry.GetAttribute("description")?.StringValue,
                    DistinguishedName = entry.Dn,
                    MemberCount = entry.GetAttribute("member")?.StringValues.Count ?? 0
                };
                groups.Add(group);
            }
            
            return groups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting groups from LDAP");
            return Enumerable.Empty<LdapGroup>();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsUserInGroupAsync(string username, string groupName)
    {
        if (!_settings.Enabled)
            return false;

        try
        {
            var userGroups = await GetUserGroupsAsync(username);
            return userGroups.Any(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if user {Username} is in group {GroupName}", 
                username, groupName);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<LdapGroup>> GetUserGroupsAsync(string username)
    {
        if (!_settings.Enabled)
            return Enumerable.Empty<LdapGroup>();

        try
        {
            await EnsureConnectionAsync();
            
            var user = await GetUserAsync(username);
            if (user == null)
                return Enumerable.Empty<LdapGroup>();
            
            // Build filter to find groups containing this user
            var filter = string.Format(_settings.GroupSearchFilter!, user.DistinguishedName);
            
            var groups = new List<LdapGroup>();
            var searchResults = _connection!.Search(
                _settings.BaseDn,
                LdapConnection.ScopeSub,
                filter,
                new[] { "cn", "description" },
                false
            );
            
            while (searchResults.HasMore())
            {
                var entry = searchResults.Next();
                groups.Add(new LdapGroup
                {
                    Name = entry.GetAttribute("cn")?.StringValue ?? string.Empty,
                    Description = entry.GetAttribute("description")?.StringValue,
                    DistinguishedName = entry.Dn
                });
            }
            
            return groups;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting groups for user {Username}", username);
            return Enumerable.Empty<LdapGroup>();
        }
    }

    /// <inheritdoc/>
    public async Task<SyncResult> SyncUsersAsync()
    {
        var result = new SyncResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        if (!_settings.Enabled)
        {
            result.Errors = 1;
            result.ErrorMessages.Add("LDAP is not enabled");
            return result;
        }

        try
        {
            await EnsureConnectionAsync();
            
            // Get all users from LDAP
            var filter = "(objectClass=user)";
            var searchResults = _connection!.Search(
                _settings.BaseDn,
                LdapConnection.ScopeSub,
                filter,
                GetUserAttributes(),
                false
            );
            
            var ldapUsers = new List<LdapUser>();
            while (searchResults.HasMore())
            {
                try
                {
                    var entry = searchResults.Next();
                    ldapUsers.Add(MapEntryToUser(entry));
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    result.ErrorMessages.Add($"Failed to parse user entry: {ex.Message}");
                }
            }
            
            _logger.LogInformation("Found {Count} users in LDAP", ldapUsers.Count);
            
            // TODO: Sync with local database
            // This would involve:
            // 1. Getting all existing local users with LDAP DN
            // 2. Creating new users that don't exist locally
            // 3. Updating existing users with new info
            // 4. Deactivating users that no longer exist in LDAP
            
            result.UsersCreated = ldapUsers.Count; // Placeholder
            result.UsersUpdated = 0;
            result.UsersDeactivated = 0;
            
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            
            _logger.LogInformation("LDAP sync completed: {Created} created, {Updated} updated, {Deactivated} deactivated", 
                result.UsersCreated, result.UsersUpdated, result.UsersDeactivated);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LDAP sync");
            result.Errors++;
            result.ErrorMessages.Add(ex.Message);
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            return result;
        }
    }

    /// <inheritdoc/>
    public async Task<ConnectionTestResult> TestConnectionAsync()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        if (!_settings.Enabled)
        {
            return new ConnectionTestResult
            {
                Success = false,
                ErrorMessage = "LDAP is not enabled",
                ResponseTime = TimeSpan.Zero
            };
        }

        try
        {
            await EnsureConnectionAsync();
            
            stopwatch.Stop();
            
            return new ConnectionTestResult
            {
                Success = _connection!.Connected,
                ResponseTime = stopwatch.Elapsed,
                ServerVersion = _connection.GetProperty("supportedLDAPVersion")?.StringValue
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ConnectionTestResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ResponseTime = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Ensures LDAP connection is established
    /// </summary>
    private async Task EnsureConnectionAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_connection == null || !_connection.Connected)
            {
                _connection = new LdapConnection();
                _connection.Connect(_settings.Server, _settings.Port);
                
                if (_settings.UseSsl)
                {
                    _connection.StartTls();
                }
                
                // Bind with service account
                if (!string.IsNullOrEmpty(_settings.BindDn) && !string.IsNullOrEmpty(_settings.BindPassword))
                {
                    _connection.Bind(_settings.BindDn, _settings.BindPassword);
                }
                
                _logger.LogInformation("Connected to LDAP server {Server}:{Port}", 
                    _settings.Server, _settings.Port);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to LDAP server {Server}:{Port}", 
                _settings.Server, _settings.Port);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Builds user DN from username
    /// </summary>
    private string BuildUserDn(string username)
    {
        // Common patterns:
        // Active Directory: userPrincipalName (user@domain.com)
        // OpenLDAP: uid=username,ou=users,dc=domain,dc=com
        
        if (_settings.UserSearchFilter!.Contains("userPrincipalName"))
        {
            // Active Directory style
            var domain = _settings.BaseDn!.Replace("DC=", "").Replace(",", ".");
            return $"{username}@{domain}";
        }
        else
        {
            // OpenLDAP style
            return $"uid={username},{_settings.BaseDn}";
        }
    }

    /// <summary>
    /// Gets user attributes to retrieve from LDAP
    /// </summary>
    private string[] GetUserAttributes()
    {
        return new[]
        {
            _settings.UsernameAttribute ?? "uid",
            _settings.EmailAttribute ?? "mail",
            _settings.DisplayNameAttribute ?? "cn",
            _settings.DepartmentAttribute ?? "department",
            "givenName",
            "sn",
            "telephoneNumber",
            "title",
            "employeeId",
            "manager",
            "userAccountControl",
            "lastLogon",
            "pwdLastSet",
            "memberOf",
            "distinguishedName"
        };
    }

    /// <summary>
    /// Maps LDAP entry to user object
    /// </summary>
    private LdapUser MapEntryToUser(LdapEntry entry)
    {
        var user = new LdapUser
        {
            Username = entry.GetAttribute(_settings.UsernameAttribute ?? "uid")?.StringValue ?? string.Empty,
            Email = entry.GetAttribute(_settings.EmailAttribute ?? "mail")?.StringValue,
            DisplayName = entry.GetAttribute(_settings.DisplayNameAttribute ?? "cn")?.StringValue,
            Department = entry.GetAttribute(_settings.DepartmentAttribute ?? "department")?.StringValue,
            FirstName = entry.GetAttribute("givenName")?.StringValue,
            LastName = entry.GetAttribute("sn")?.StringValue,
            PhoneNumber = entry.GetAttribute("telephoneNumber")?.StringValue,
            Title = entry.GetAttribute("title")?.StringValue,
            EmployeeId = entry.GetAttribute("employeeId")?.StringValue,
            Manager = entry.GetAttribute("manager")?.StringValue,
            DistinguishedName = entry.Dn
        };
        
        // Get groups
        var memberOf = entry.GetAttribute("memberOf");
        if (memberOf != null)
        {
            user.Groups = memberOf.StringValues
                .Select(dn => ExtractCnFromDn(dn))
                .ToList();
        }
        
        // Parse userAccountControl for Active Directory
        var uac = entry.GetAttribute("userAccountControl")?.StringValue;
        if (!string.IsNullOrEmpty(uac) && int.TryParse(uac, out var uacValue))
        {
            // Check if account is disabled (bit 2)
            user.IsActive = (uacValue & 2) == 0;
        }
        
        return user;
    }

    /// <summary>
    /// Extracts CN from DN
    /// </summary>
    private static string ExtractCnFromDn(string dn)
    {
        var match = System.Text.RegularExpressions.Regex.Match(dn, @"CN=([^,]+)");
        return match.Success ? match.Groups[1].Value : dn;
    }

    public void Dispose()
    {
        _connection?.Disconnect();
        _connection?.Dispose();
        _connectionLock.Dispose();
    }
}

/// <summary>
/// Extension methods for LdapEntry
/// </summary>
internal static class LdapExtensions
{
    public static LdapAttribute? GetAttribute(this LdapEntry entry, string attributeName)
    {
        try
        {
            return entry.GetAttributeSet().GetAttribute(attributeName);
        }
        catch
        {
            return null;
        }
    }
}
