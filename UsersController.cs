using GR.Core;
using GR.Core.Attributes;
using GR.Core.BaseControllers;
using GR.Core.Extensions;
using GR.Core.Helpers;
using GR.Entities.Data;
using GR.Identity.Abstractions;
using GR.Identity.Abstractions.Enums;
using GR.Identity.Abstractions.Events;
using GR.Identity.Abstractions.Events.EventArgs.Users;
using GR.Identity.Abstractions.Models.AddressModels;
using GR.Identity.Abstractions.Models.MultiTenants;
using GR.Identity.Abstractions.ViewModels.UserProfileAddress;
using GR.Identity.Data;
using GR.Identity.Data.Permissions;
using GR.Identity.LdapAuth.Abstractions;
using GR.Identity.LdapAuth.Abstractions.Models;
using GR.Identity.Permissions.Abstractions.Attributes;
using GR.Identity.Razor.Users.ViewModels.UserProfileViewModels;
using GR.Identity.Razor.Users.ViewModels.UserViewModels;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using GR.Core.Helpers.Pagination;
using GR.Core.Helpers.Responses;
using GR.Crm.Abstractions;
using GR.MultiTenant.Abstractions;
using GR.MultiTenant.Abstractions.ViewModels;
using GR.Notifications.Abstractions;
using UserProfileViewModel = GR.Identity.Razor.Users.ViewModels.UserProfileViewModels.UserProfileViewModel;
using GR.Identity.LdapAuth.Abstractions.ViewModels;

namespace GR.Identity.Razor.Users.Controllers
{
    public class UsersController : BaseIdentityController<ApplicationDbContext, EntitiesDbContext, GearUser,
        GearRole, Tenant, INotify<GearRole>>
    {
        #region Injections

        /// <summary>
        /// Inject user manager
        /// </summary>
        private readonly IUserManager<GearUser> _userManager;

        /// <summary>
        /// Logger
        /// </summary>
        private ILogger<UsersController> Logger { get; }

        /// <summary>
        /// Inject Ldap User Manager
        /// </summary>
        private readonly BaseLdapUserManager<LdapUser> _ldapUserManager;

        /// <summary>
        /// Inject organization service
        /// </summary>
        private readonly IOrganizationService<Tenant> _organizationService;

        private readonly IStringLocalizer _localizer;

        /// <summary>
        /// Ldap service
        /// </summary>
        private readonly ILdapService<LdapUser> _ldapService;
        /// <summary>
        /// Inject mapper
        /// </summary>
        private readonly IMapper _mapper;

        /// <summary>
        /// Inject mapper
        /// </summary>
        private readonly IVocabulariesService _jobPositionService;

        #endregion Injections
        public UsersController(UserManager<GearUser> userManager, RoleManager<GearRole> roleManager,
            ApplicationDbContext applicationDbContext, EntitiesDbContext context,
            INotify<GearRole> notify, BaseLdapUserManager<LdapUser> ldapUserManager,
            ILogger<UsersController> logger,
            IStringLocalizer localizer,
            IUserManager<GearUser> userManager1,
            IOrganizationService<Tenant> organizationService,
            ILdapService<LdapUser> ldapService,
            IMapper mapper,
            IVocabulariesService jobPositionService)
            : base(userManager, roleManager, applicationDbContext, context, notify)
        {
            _ldapUserManager = ldapUserManager;
            Logger = logger;
            _localizer = localizer;
            _organizationService = organizationService;
            _ldapService = ldapService;
            _mapper = mapper;
            _jobPositionService = jobPositionService;
            _userManager = userManager1;
        }

        /// <summary>
        /// User list for admin visualization
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserRead)]
        public IActionResult Index(int nr = 1)
        {
            return View(nr);
        }

        /// <summary>
        /// Create new user
        /// </summary>
        /// <returns></returns>
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserCreate)]
        public virtual async Task<IActionResult> Create()
        {
            var model = new CreateUserViewModel
            {
                Roles = await GetRoleSelectListItemAsync(),
                Groups = await GetAuthGroupSelectListItemAsync(),
                Tenants = await GetTenantsSelectListItemAsync(),
                CountrySelectListItems = await GetCountrySelectList()
            };
            return View(model);
        }

        /// <summary>
        /// Apply user create
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserCreate)]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.Roles = await GetRoleSelectListItemAsync();
                model.Groups = await GetAuthGroupSelectListItemAsync();
                model.Tenants = await GetTenantsSelectListItemAsync();
                model.CountrySelectListItems = await GetCountrySelectList();
                return View(model);
            }

            var user = new GearUser
            {
                UserName = model.UserName,
                Email = model.Email,
                Created = DateTime.Now,
                Changed = DateTime.Now,
                IsDeleted = model.IsDeleted,
                Author = User.Identity.Name,
                AuthenticationType = model.AuthenticationType,
                IsEditable = true,
                TenantId = model.TenantId,
                LastPasswordChanged = DateTime.Now,
                UserFirstName = model.FirstName,
                UserLastName = model.LastName,
                Birthday = model.Birthday ?? DateTime.MinValue,
                AboutMe = model.AboutMe,
                PhoneNumber = model.PhoneNumber,
                DialCode = model.DialCode
            };

            if (model.UserPhoto != null)
            {
                using (var memoryStream = new MemoryStream())
                {
                    await model.UserPhoto.CopyToAsync(memoryStream);
                    user.UserPhoto = memoryStream.ToArray();
                }
            }

            var result = await UserManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                model.Roles = await GetRoleSelectListItemAsync();
                model.Groups = await GetAuthGroupSelectListItemAsync();
                model.Tenants = await GetTenantsSelectListItemAsync();
                model.CountrySelectListItems = await GetCountrySelectList();
                return View(model);
            }

            Logger.LogInformation("User {0} created successfully", user.UserName);

            if (model.SelectedRoleId != null && model.SelectedRoleId.Any())
            {
                var rolesNameList = await RoleManager.Roles.Where(x => model.SelectedRoleId.Contains(x.Id))
                    .Select(x => x.Name).ToListAsync();
                var roleAddResult = await UserManager.AddToRolesAsync(user, rolesNameList);
                if (!roleAddResult.Succeeded)
                {
                    foreach (var error in roleAddResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    model.Roles = await GetRoleSelectListItemAsync();
                    model.Groups = await GetAuthGroupSelectListItemAsync();
                    model.Tenants = await GetTenantsSelectListItemAsync();
                    model.CountrySelectListItems = await GetCountrySelectList();
                    return View(model);
                }
            }

            if (model.SelectedGroupId != null && model.SelectedGroupId.Any())
            {
                var userGroupList = model.SelectedGroupId
                    .Select(_ => new UserGroup { AuthGroupId = Guid.Parse(_), UserId = user.Id.ToString() }).ToList();

                await ApplicationDbContext.UserGroups.AddRangeAsync(userGroupList);
            }
            else
            {
                var groupId = await ApplicationDbContext.AuthGroups.FirstOrDefaultAsync();
                if (groupId != null)
                {
                    ApplicationDbContext.UserGroups.Add(new UserGroup
                    {
                        AuthGroupId = groupId.Id,
                        UserId = user.Id
                    });
                }
            }

            var dbResult = await ApplicationDbContext.SaveAsync();
            if (!dbResult.IsSuccess)
            {
                ModelState.AppendResultModelErrors(dbResult.Errors);
                model.Roles = await GetRoleSelectListItemAsync();
                model.Groups = await GetAuthGroupSelectListItemAsync();
                model.Tenants = await GetTenantsSelectListItemAsync();
                model.CountrySelectListItems = await GetCountrySelectList();
                return View(model);
            }

            IdentityEvents.Users.UserCreated(new UserCreatedEventArgs
            {
                Email = user.Email,
                UserName = user.UserName,
                UserId = user.Id.ToGuid()
            });

            return RedirectToAction(nameof(Index), "Users");
        }

        /// <summary>
        /// Get Ad users
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [AjaxOnly]
        public JsonResult GetAdUsers()
        {
            var result = new ResultModel<IEnumerable<LdapUser>>();
            var addedUsers = ApplicationDbContext.Users.Where(x => x.AuthenticationType.Equals(AuthenticationType.Ad)).Adapt<IEnumerable<LdapUser>>()
                .ToList();
            var users = _ldapUserManager.Users;
            if (addedUsers.Any())
            {
                users = users.Except(addedUsers);
            }

            result.IsSuccess = true;
            result.Result = users;
            return Json(users);
        }

        /// <summary>
        /// Add Ad user
        /// </summary>
        /// <param name="userName"></param>
        /// <returns></returns>
        [HttpPost]
        [AjaxOnly]
        public virtual async Task<JsonResult> AddAdUser([Required] string userName)
        {
            var result = new ResultModel<Guid>();
            if (string.IsNullOrEmpty(userName))
            {
                result.Errors.Add(new ErrorModel(string.Empty, $"Invalid username : {userName}"));
                return Json(result);
            }

            var exists = await UserManager.FindByNameAsync(userName);
            if (exists != null)
            {
                result.Errors.Add(new ErrorModel(string.Empty, $"UserName {userName} exists!"));
                return Json(result);
            }

            var user = new GearUser();

            var ldapUser = await _ldapUserManager.FindByNameAsync(userName);
            if (ldapUser == null)
            {
                result.Errors.Add(new ErrorModel(string.Empty, $"There is no AD user with this username : {userName}"));
                return Json(result);
            }

            user.Id = Guid.NewGuid().ToString();
            user.UserName = ldapUser.SamAccountName;
            user.Email = ldapUser.EmailAddress;
            user.AuthenticationType = AuthenticationType.Ad;
            user.Created = DateTime.Now;
            user.Author = User.Identity.Name;
            user.Changed = DateTime.Now;
            user.TenantId = CurrentUserTenantId;
            result.IsSuccess = true;
            var req = await UserManager.CreateAsync(user, "ldap_default_password");
            if (!req.Succeeded)
            {
                result.Errors.Add(new ErrorModel(string.Empty, $"Fail to add user : {userName}"));
                result.IsSuccess = false;
            }
            else
            {
                IdentityEvents.Users.UserCreated(new UserCreatedEventArgs
                {
                    Email = user.Email,
                    UserName = user.UserName,
                    UserId = user.Id.ToGuid()
                });
                result.Result = Guid.Parse(user.Id);
            }

            return Json(result);
        }

        [HttpGet]
        [Route("api/[controller]/[action]")]
        public virtual async Task<JsonResult> GetUserRolesByUserId(string UserId)
        {
            var user = await _userManager.UserManager.FindByIdAsync(UserId);
            var userRoles = await _userManager.GetUserRolesAsync(user);
            return Json(new SuccessResultModel<IEnumerable<GearRole>>(userRoles));
        }

        [HttpPost]
        [Route("api/[controller]/[action]")]
        [ValidateAntiForgeryToken]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserUpdate)]
        public virtual async Task<JsonResult> UpdateUserRolesByUserId(UpdateUserRoleViewModel model)
        {
            var user = await _userManager.UserManager.FindByIdAsync(model.UserId);
            if (user == null)
            {
                return Json(new NotFoundResultModel());
            }

            var userRoles = await UserManager.GetRolesAsync(user);
            var response = new ResultModel { IsSuccess = false};

            var removeResult = await UserManager.RemoveFromRolesAsync(user, userRoles);
            if (removeResult.Succeeded)
            {

                var roleNameList = await RoleManager.Roles.Where(x => model.RoleIds.Contains(x.Id)).Select(x => x.Name).ToListAsync();
                var addResult = await UserManager.AddToRolesAsync(user, roleNameList);
                if (addResult.Succeeded)
                    return Json(new ResultModel { IsSuccess = true });

                foreach (var error in addResult.Errors)
                {
                    response.Errors.Add(new ErrorModel { Message = error.Description });
                }
                return Json(response);
            }

            foreach(var error in removeResult.Errors)
            {
                response.Errors.Add(new ErrorModel { Message = error.Description });
            }
            return Json(response);
        }

        /// <summary>
        /// Delete user form DB
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPost]
        [ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserDelete)]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            if (id.IsNullOrEmpty())
            {
                return Json(new { success = false, message = "Id is null" });
            }

            if (IsCurrentUser(id))
            {
                return Json(new { success = false, message = "You can't delete current user" });
            }

            var applicationUser = await ApplicationDbContext.Users.SingleOrDefaultAsync(m => m.Id == id);
            if (applicationUser == null)
            {
                return Json(new { success = false, message = "User not found" });
            }

            if (applicationUser.IsEditable == false)
            {
                return Json(new { succsess = false, message = "Is system user!!!" });
            }

            try
            {
                await UserManager.UpdateSecurityStampAsync(applicationUser);
                await UserManager.DeleteAsync(applicationUser);
                IdentityEvents.Users.UserDelete(new UserDeleteEventArgs
                {
                    Email = applicationUser.Email,
                    UserName = applicationUser.UserName,
                    UserId = applicationUser.Id.ToGuid()
                });
                return Json(new { success = true, message = "Delete success" });
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message);
                return Json(new { success = false, message = "Error on delete!!!" });
            }
        }

        /// <summary>
        ///  Edit user
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserUpdate)]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var applicationUser = await ApplicationDbContext.Users.SingleOrDefaultAsync(m => m.Id == id);
            if (applicationUser == null)
            {
                return NotFound();
            }

            var roles = RoleManager.Roles.AsEnumerable();
            var groups = ApplicationDbContext.AuthGroups.AsEnumerable();
            var userGroup = ApplicationDbContext.UserGroups.Where(x => x.UserId == applicationUser.Id).ToList()
                .Select(s => s.AuthGroupId.ToString()).ToList();
            var userRolesNames = await UserManager.GetRolesAsync(applicationUser);
            var userRoles = userRolesNames.Select(item => roles.FirstOrDefault(x => x.Name == item)?.Id.ToString())
                .ToList();

            var model = new UpdateUserViewModel
            {
                Id = applicationUser.Id.ToGuid(),
                Email = applicationUser.Email,
                IsDeleted = applicationUser.IsDeleted,
                Password = applicationUser.PasswordHash,
                RepeatPassword = applicationUser.PasswordHash,
                UserName = applicationUser.UserName,
                UserNameOld = applicationUser.UserName,
                Roles = roles,
                Groups = groups,
                SelectedGroupId = userGroup,
                SelectedRoleId = userRoles,
                UserPhoto = applicationUser.UserPhoto,
                AuthenticationType = applicationUser.AuthenticationType,
                TenantId = applicationUser.TenantId,
                Tenants = ApplicationDbContext.Tenants.AsNoTracking().Where(x => !x.IsDeleted).ToList(),
                FirstName = applicationUser.UserFirstName,
                LastName = applicationUser.UserLastName,
                PhoneNumber = applicationUser.PhoneNumber,
                DialCode = applicationUser.DialCode
            };
            return View(model);
        }

        /// <summary>
        ///     Save user data
        /// </summary>
        /// <param name="id"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserUpdate)]
        public virtual async Task<IActionResult> Edit(string id, UpdateUserViewModel model)
        {
            if (Guid.Parse(id) != model.Id)
            {
                return NotFound();
            }

            var applicationUser = await ApplicationDbContext.Users.SingleOrDefaultAsync(m => m.Id == id);
            var roles = RoleManager.Roles.AsEnumerable();
            var groupsList = ApplicationDbContext.AuthGroups.AsEnumerable();
            var userGroupListList = ApplicationDbContext.UserGroups.Where(x => x.UserId == applicationUser.Id).ToList()
                .Select(s => s.AuthGroupId.ToString()).ToList();
            var userRolesNames = await UserManager.GetRolesAsync(applicationUser);
            var userRoleList = userRolesNames
                .Select(item => roles.FirstOrDefault(x => x.Name == item)?.Id.ToString())
                .ToList();

            model.Roles = roles;
            model.Groups = groupsList;
            model.SelectedGroupId = userGroupListList;
            model.Tenants = ApplicationDbContext.Tenants.Where(x => !x.IsDeleted).ToList();

            if (!ModelState.IsValid)
            {
                model.SelectedRoleId = userRoleList;
                foreach (var error in ViewData.ModelState.Values.SelectMany(stateValue => stateValue.Errors))
                {
                    ModelState.AddModelError(string.Empty, error.ErrorMessage);
                }

                return View(model);
            }

            // Update User Data
            var user = await UserManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (model.AuthenticationType.Equals(AuthenticationType.Ad))
            {
                var ldapUser = await _ldapUserManager.FindByNameAsync(model.UserName);
                if (ldapUser == null)
                {
                    model.SelectedRoleId = userRoleList;
                    ModelState.AddModelError("", $"There is no AD user with this username : {model.UserName}");
                    return View(model);
                }

                user.UserName = ldapUser.SamAccountName;
                user.Email = ldapUser.EmailAddress;
            }

            user.IsDeleted = model.IsDeleted;
            user.Changed = DateTime.Now;
            user.Email = model.Email;
            user.ModifiedBy = User.Identity.Name;
            user.UserName = model.UserName;
            user.TenantId = model.TenantId;
            user.UserFirstName = model.FirstName;
            user.UserLastName = model.LastName;
            user.PhoneNumber = model.PhoneNumber;
            user.DialCode = model.DialCode;

            if (model.UserPhotoUpdateFile != null)
            {
                using (var memoryStream = new MemoryStream())
                {
                    await model.UserPhotoUpdateFile.CopyToAsync(memoryStream);
                    user.UserPhoto = memoryStream.ToArray();
                }
            }

            var result = await UserManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                model.SelectedRoleId = userRoleList;
                foreach (var _ in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, _.Description);
                }

                return View(model);
            }

            // REFRESH USER ROLES
            var userRoles = await ApplicationDbContext.UserRoles.Where(x => x.UserId == user.Id).ToListAsync();
            var rolesList = new List<string>();
            foreach (var _ in userRoles)
            {
                var role = await RoleManager.FindByIdAsync(_.RoleId);
                rolesList.Add(role.Name);
            }

            await UserManager.RemoveFromRolesAsync(user, rolesList);

            var roleNameList = new List<string>();
            foreach (var _ in model.SelectedRoleId)
            {
                var role = await RoleManager.FindByIdAsync(_);
                roleNameList.Add(role.Name);
            }

            await UserManager.AddToRolesAsync(user, roleNameList);

            if (model.Groups != null && model.Groups.Any())
            {
                //Refresh groups
                var currentGroupsList =
                    await ApplicationDbContext.UserGroups.Where(x => x.UserId == user.Id).ToListAsync();
                ApplicationDbContext.UserGroups.RemoveRange(currentGroupsList);

                var userGroupList = model.SelectedGroupId
                    .Select(groupId => new UserGroup { UserId = user.Id, AuthGroupId = Guid.Parse(groupId) }).ToList();
                await ApplicationDbContext.UserGroups.AddRangeAsync(userGroupList);
            }

            await UserManager.UpdateSecurityStampAsync(user);

            //Refresh user claims for this user
            //await user.RefreshClaims(Context, signInManager);
            IdentityEvents.Users.UserUpdated(new UserUpdatedEventArgs
            {
                Email = user.Email,
                UserName = user.UserName,
                UserId = user.Id.ToGuid()
            });
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Return list of State Or Provinces by country id
        /// </summary>
        /// <param name="countryId"></param>
        /// <returns></returns>
        [HttpGet, AllowAnonymous]
        public virtual JsonResult GetCityByCountryId([Required] string countryId)
        {
            var resultModel = new ResultModel<IEnumerable<SelectListItem>>();
            if (string.IsNullOrEmpty(countryId))
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "Country id is null"));
                return Json(resultModel);
            }

            var citySelectList = ApplicationDbContext.StateOrProvinces
                .AsNoTracking()
                .Where(x => x.CountryId.Equals(countryId))
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = x.Name
                }).ToList();
            citySelectList.Insert(0, new SelectListItem("Select city", string.Empty));

            resultModel.Result = citySelectList;
            resultModel.IsSuccess = true;
            return Json(resultModel);
        }

        /// <summary>
        /// User profile info
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public virtual async Task<IActionResult> Profile()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                return NotFound();
            }

            var model = new UserProfileViewModel
            {
                UserId = currentUser.Id.ToGuid(),
                TenantId = currentUser.TenantId ?? Guid.Empty,
                UserName = currentUser.UserName,
                UserFirstName = currentUser.UserFirstName,
                UserLastName = currentUser.UserLastName,
                UserPhoneNumber = currentUser.PhoneNumber,
                AboutMe = currentUser.AboutMe,
                Birthday = currentUser.Birthday,
                Email = currentUser.Email,
                PhoneNumber = currentUser.PhoneNumber,
                DialCode = currentUser.DialCode,
                Roles = await UserManager.GetRolesAsync(currentUser),
                Groups = await ApplicationDbContext.UserGroups
                    .Include(x => x.AuthGroup)
                    .Where(x => x.UserId.Equals(currentUser.Id))
                    .Select(x => x.AuthGroup.Name)
                    .ToListAsync()
            };
            return View(model);
        }

        /// <summary>
        /// Get view for edit profile info
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        [HttpGet]
        public virtual async Task<IActionResult> EditProfile(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return NotFound();
            }

            var currentUser = await UserManager.Users.FirstOrDefaultAsync(x => x.Id.Equals(userId));
            if (currentUser == null)
            {
                return NotFound();
            }

            var model = new UserProfileEditViewModel
            {
                Id = currentUser.Id,
                UserFirstName = currentUser.UserFirstName,
                UserLastName = currentUser.UserLastName,
                Birthday = currentUser.Birthday,
                AboutMe = currentUser.AboutMe,
                UserPhoneNumber = currentUser.PhoneNumber,
                DialCode = currentUser.DialCode
            };
            return PartialView("Partial/_EditProfilePartial", model);
        }

        /// <summary>
        /// Update user profile info
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual async Task<JsonResult> EditProfile(UserProfileEditViewModel model)
        {
            var resultModel = new ResultModel();
            if (!ModelState.IsValid)
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "Invalid model"));
                return Json(resultModel);
            }

            var currentUser = await UserManager.Users.FirstOrDefaultAsync(x => x.Id.Equals(model.Id));
            if (currentUser == null)
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "User not found!"));
                return Json(resultModel);
            }

            currentUser.UserFirstName = model.UserFirstName;
            currentUser.UserLastName = model.UserLastName;
            currentUser.Birthday = model.Birthday;
            currentUser.AboutMe = model.AboutMe;
            currentUser.PhoneNumber = model.UserPhoneNumber;
            currentUser.DialCode = model.DialCode;

            var result = await UserManager.UpdateAsync(currentUser);
            if (result.Succeeded)
            {
                resultModel.IsSuccess = true;
                return Json(resultModel);
            }

            foreach (var identityError in result.Errors)
            {
                resultModel.Errors.Add(new ErrorModel(identityError.Code, identityError.Description));
            }

            return Json(resultModel);
        }

        /// <summary>
        /// Get view for change user password
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="callBackUrl"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> ChangeUserPassword([Required] Guid? userId, string callBackUrl)
        {
            if (userId == null) return NotFound();
            var user = await UserManager.FindByIdAsync(userId.Value.ToString());
            if (user == null) return NotFound();
            return View(new ChangeUserPasswordViewModel
            {
                Email = user.Email,
                UserName = user.UserName,
                AuthenticationType = user.AuthenticationType,
                UserId = user.Id.ToGuid(),
                CallBackUrl = callBackUrl
            });
        }

        /// <summary>
        /// Apply new password
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        public virtual async Task<IActionResult> ChangeUserPassword([Required] ChangeUserPasswordViewModel model)
        {
            if (model.AuthenticationType.Equals(AuthenticationType.Ad))
            {
                var ldapUser = await _ldapUserManager.FindByNameAsync(model.UserName);

                var bind = await _ldapUserManager.CheckPasswordAsync(ldapUser, model.Password);
                if (!bind)
                {
                    ModelState.AddModelError("", $"Invalid credentials for AD authentication");
                    return View(model);
                }
            }

            var user = await UserManager.FindByIdAsync(model.UserId.ToString());
            if (user == null)
            {
                ModelState.AddModelError("", "The user is no longer in the system");
                return View(model);
            }

            var hasher = new PasswordHasher<GearUser>();
            var hashedPassword = hasher.HashPassword(user, model.Password);
            user.PasswordHash = hashedPassword;
            user.LastPasswordChanged = DateTime.Now;
            var result = await UserManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                IdentityEvents.Users.UserPasswordChange(new UserChangePasswordEventArgs
                {
                    Email = user.Email,
                    UserName = user.UserName,
                    UserId = user.Id.ToGuid(),
                    Password = model.Password
                });
                return Redirect(model.CallBackUrl);
            }

            foreach (var _ in result.Errors)
            {
                ModelState.AddModelError(string.Empty, _.Description);
            }

            return View(model);
        }

        /// <summary>
        /// Load user with ajax
        /// </summary>
        /// <param name="hub"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        [HttpPost]
        [AjaxOnly]
        public JsonResult LoadUsers([FromServices] ICommunicationHub hub, DTParameters param)
        {
            var filtered = GetUsersFiltered(param.Search.Value, param.SortOrder, param.Start, param.Length,
                out var totalCount);

            var usersList = filtered.Select(async o =>
            {
                var sessions = hub.GetSessionsCountByUserId(Guid.Parse(o.Id));
                var roles = await UserManager.GetRolesAsync(o);
                var org = await ApplicationDbContext.Tenants.FirstOrDefaultAsync(x => x.Id == o.TenantId);
                return new UserListItemViewModel
                {
                    Id = o.Id,
                    UserName = o.UserName,
                    CreatedDate = o.Created.ToShortDateString(),
                    CreatedBy = o.Author,
                    ModifiedBy = o.ModifiedBy,
                    Changed = o.Changed.ToShortDateString(),
                    Roles = roles,
                    Sessions = sessions,
                    AuthenticationType = o.AuthenticationType.ToString(),
                    LastLogin = o.LastLogin,
                    Organization = org?.Name
                };
            }).Select(x => x.Result);

            var finalResult = new DTResult<UserListItemViewModel>
            {
                Draw = param.Draw,
                Data = usersList.ToList(),
                RecordsFiltered = totalCount,
                RecordsTotal = filtered.Count
            };

            return Json(finalResult);
        }

        /// <summary>
        /// Get application users list filtered
        /// </summary>
        /// <param name="search"></param>
        /// <param name="sortOrder"></param>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <param name="totalCount"></param>
        /// <returns></returns>
        [NonAction]
        private List<GearUser> GetUsersFiltered(string search, string sortOrder, int start, int length,
            out int totalCount)
        {
            var result = ApplicationDbContext.Users.AsNoTracking()
                .Where(p =>
                    search == null || p.Email != null &&
                    p.Email.ToLower().Contains(search.ToLower()) || p.UserName != null &&
                    p.UserName.ToLower().Contains(search.ToLower()) ||
                    p.ModifiedBy != null && p.ModifiedBy.ToLower().Contains(search.ToLower())).ToList();
            totalCount = result.Count;

            result = result.Skip(start).Take(length).ToList();
            switch (sortOrder)
            {
                case "email":
                    result = result.OrderBy(a => a.Email).ToList();
                    break;

                case "created":
                    result = result.OrderBy(a => a.Created).ToList();
                    break;

                case "userName":
                    result = result.OrderBy(a => a.UserName).ToList();
                    break;

                case "author":
                    result = result.OrderBy(a => a.Author).ToList();
                    break;

                case "changed":
                    result = result.OrderBy(a => a.Changed).ToList();
                    break;

                case "email DESC":
                    result = result.OrderByDescending(a => a.Email).ToList();
                    break;

                case "created DESC":
                    result = result.OrderByDescending(a => a.Created).ToList();
                    break;

                case "userName DESC":
                    result = result.OrderByDescending(a => a.UserName).ToList();
                    break;

                case "author DESC":
                    result = result.OrderByDescending(a => a.Author).ToList();
                    break;

                case "changed DESC":
                    result = result.OrderByDescending(a => a.Changed).ToList();
                    break;

                default:
                    result = result.AsQueryable().ToList();
                    break;
            }

            return result.ToList();
        }

        /// <summary>
        /// Check if is current user
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private bool IsCurrentUser(string id)
        {
            return id.Equals(User.Identity.Name);
        }

        /// <summary>
        /// Get user image
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [AllowAnonymous]
        public IActionResult GetImage(string id)
        {
            if (id.IsNullOrEmpty())
            {
                return NotFound();
            }

            try
            {
                var photo = ApplicationDbContext.Users.SingleOrDefault(x => x.Id == id);
                if (photo?.UserPhoto != null) return File(photo.UserPhoto, "image/jpg");
                var def = GetDefaultImage();
                if (def == null) return NotFound();
                return File(def, "image/jpg");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return NotFound();
        }

        /// <summary>
        /// Get default user image
        /// </summary>
        /// <returns></returns>
        private static byte[] GetDefaultImage()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Static/Embedded Resources/user.jpg");
            if (!System.IO.File.Exists(path))
                return default;

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var binary = new BinaryReader(stream))
                {
                    var data = binary.ReadBytes((int)stream.Length);
                    return data;
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }

            return default;
        }

        /// <summary>
        /// Validate user name
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="userNameOld"></param>
        /// <returns></returns>
        [AcceptVerbs("Get", "Post")]
        public async Task<IActionResult> VerifyName(string userName, string userNameOld)
        {
            if (userNameOld != null && userName.ToLower().Equals(userNameOld.ToLower()))
            {
                return Json(true);
            }

            if (await ApplicationDbContext.Users
                .AsNoTracking()
                .AnyAsync(x => x.UserName.ToLower().Equals(userName.ToLower())))
            {
                return Json($"User name {userName} is already in use.");
            }

            return Json(true);
        }

        [HttpPost]
        public virtual async Task<JsonResult> DeleteUserAddress(Guid? id)
        {
            var resultModel = new ResultModel();
            if (!id.HasValue)
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "Null id"));
                return Json(resultModel);
            }

            var currentAddress = await ApplicationDbContext.Addresses.FindAsync(id.Value);
            if (currentAddress == null)
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "Address not found"));
                return Json(resultModel);
            }

            currentAddress.IsDeleted = true;
            var result = await ApplicationDbContext.SaveAsync();
            if (!result.IsSuccess)
            {
                foreach (var error in result.Errors)
                {
                    resultModel.Errors.Add(new ErrorModel(error.Key, error.Message));
                }

                return Json(resultModel);
            }

            resultModel.IsSuccess = true;
            return Json(resultModel);
        }

        [HttpGet]
        public virtual PartialViewResult UserPasswordChange()
        {
            return PartialView("Partial/_ChangePassword");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual async Task<JsonResult> UserPasswordChange(ChangePasswordViewModel model)
        {
            var resultModel = new ResultModel();
            if (!ModelState.IsValid)
            {
                resultModel.Errors.Add(new ErrorModel { Key = string.Empty, Message = "Invalid model" });
                return Json(resultModel);
            }

            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                resultModel.Errors.Add(new ErrorModel { Key = string.Empty, Message = "User not found" });
                return Json(resultModel);
            }

            var result = await UserManager.ChangePasswordAsync(currentUser, model.CurrentPassword, model.Password);
            if (result.Succeeded)
            {
                resultModel.IsSuccess = true;
                IdentityEvents.Users.UserPasswordChange(new UserChangePasswordEventArgs
                {
                    Email = currentUser.Email,
                    UserName = currentUser.UserName,
                    UserId = currentUser.Id.ToGuid(),
                    Password = model.Password
                });
                return Json(resultModel);
            }

            resultModel.Errors.Add(new ErrorModel { Key = string.Empty, Message = "Error on change password" });
            return Json(resultModel);
        }

        #region Partial Views

        [HttpGet]
        public virtual async Task<IActionResult> UserOrganizationPartial(Guid? tenantId)
        {
            if (!tenantId.HasValue)
            {
                return NotFound();
            }

            var tenant = await ApplicationDbContext.Tenants.FindAsync(tenantId);
            if (tenant == null)
            {
                return NotFound();
            }

            var model = new UserProfileTenantViewModel
            {
                Name = tenant.Name,
                TenantId = tenant.TenantId,
                Description = tenant.Description,
                Address = tenant.Address,
                SiteWeb = tenant.SiteWeb
            };
            return PartialView("Partial/_OrganizationPartial", model);
        }

        [HttpGet]
        public virtual IActionResult UserAddressPartial(Guid? userId)
        {
            if (!userId.HasValue)
            {
                return NotFound();
            }

            var addressList = ApplicationDbContext.Addresses
                .AsNoTracking()
                .Where(x => x.ApplicationUserId.Equals(userId.Value.ToString()) && x.IsDeleted == false)
                .Include(x => x.Country)
                .Include(x => x.StateOrProvince)
                .Include(x => x.District)
                .Select(address => new UserProfileAddressViewModel
                {
                    Id = address.Id,
                    AddressLine1 = address.AddressLine1,
                    AddressLine2 = address.AddressLine2,
                    Phone = address.Phone,
                    ContactName = address.ContactName,
                    District = address.District.Name,
                    Country = address.Country.Name,
                    City = address.StateOrProvince.Name,
                    IsPrimary = address.IsDefault,
                    ZipCode = address.ZipCode,
                })
                .ToList();

            return PartialView("Partial/_AddressListPartial", addressList);
        }

        [HttpGet]
        public virtual PartialViewResult ChangeUserPasswordPartial()
        {
            return PartialView("Partial/_ChangePasswordPartial");
        }

        [HttpGet]
        public virtual async Task<IActionResult> AddUserProfileAddress()
        {
            var model = new AddUserProfileAddressViewModel
            {
                CountrySelectListItems = await GetCountrySelectList()
            };
            return PartialView("Partial/_AddUserProfileAddress", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual async Task<JsonResult> AddUserProfileAddress(AddUserProfileAddressViewModel model)
        {
            var resultModel = new ResultModel();

            if (!ModelState.IsValid)
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "Invalid model"));
                return Json(resultModel);
            }

            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "User not found"));
                return Json(resultModel);
            }

            var address = new Address
            {
                AddressLine1 = model.AddressLine1,
                AddressLine2 = model.AddressLine2,
                Created = DateTime.Now,
                ContactName = model.ContactName,
                ZipCode = model.ZipCode,
                Phone = model.Phone,


                CountryId = model.SelectedCountryId,
                StateOrProvinceId = model.SelectedStateOrProvinceId,
                ApplicationUser = currentUser,
                IsDefault = model.IsDefault
            };

            if (model.IsDefault)
            {
                ApplicationDbContext.Addresses
                    .Where(x => x.ApplicationUserId.Equals(currentUser.Id))
                    .ToList().ForEach(b => b.IsDefault = false);
            }

            await ApplicationDbContext.AddAsync(address);
            var result = await ApplicationDbContext.SaveAsync();
            if (!result.IsSuccess)
            {
                foreach (var resultError in result.Errors)
                {
                    resultModel.Errors.Add(new ErrorModel(resultError.Key, resultError.Message));
                }

                return Json(resultModel);
            }

            resultModel.IsSuccess = true;
            return Json(resultModel);
        }

        [HttpGet]
        public virtual async Task<IActionResult> EditUserProfileAddress(Guid? addressId)
        {
            if (!addressId.HasValue)
            {
                return NotFound();
            }

            var currentAddress = await ApplicationDbContext.Addresses
                .FirstOrDefaultAsync(x => x.Id.Equals(addressId.Value));
            var cityBySelectedCountry = await ApplicationDbContext.StateOrProvinces
                .AsNoTracking()
                .Where(x => x.CountryId.Equals(currentAddress.CountryId))
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = x.Name
                }).ToListAsync();
            if (currentAddress == null)
            {
                return NotFound();
            }

            var model = new EditUserProfileAddressViewModel
            {
                Id = currentAddress.Id,
                CountrySelectListItems = await GetCountrySelectList(),
                AddressLine1 = currentAddress.AddressLine1,
                AddressLine2 = currentAddress.AddressLine2,
                Phone = currentAddress.Phone,
                ContactName = currentAddress.ContactName,
                ZipCode = currentAddress.ZipCode,
                SelectedCountryId = currentAddress.CountryId,
                SelectedStateOrProvinceId = currentAddress.StateOrProvinceId,
                SelectedStateOrProvinceSelectListItems = cityBySelectedCountry,
                IsDefault = currentAddress.IsDefault
            };
            return PartialView("Partial/_EditUserProfileAddress", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public virtual async Task<JsonResult> EditUserProfileAddress(EditUserProfileAddressViewModel model)
        {
            var resultModel = new ResultModel();

            if (!ModelState.IsValid)
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "Invalid model"));
                return Json(resultModel);
            }

            var currentAddress = await ApplicationDbContext.Addresses.FirstOrDefaultAsync(x => x.Id.Equals(model.Id));
            if (currentAddress == null)
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "Address not found"));
                return Json(resultModel);
            }

            if (model.IsDefault)
            {
                ApplicationDbContext.Addresses
                    .Where(x => x.ApplicationUserId.Equals(currentAddress.ApplicationUserId))
                    .ToList().ForEach(b => b.IsDefault = false);
            }

            currentAddress.CountryId = model.SelectedCountryId;
            currentAddress.StateOrProvinceId = model.SelectedStateOrProvinceId;
            currentAddress.AddressLine1 = model.AddressLine1;
            currentAddress.AddressLine2 = model.AddressLine2;
            currentAddress.ContactName = model.ContactName;
            currentAddress.Phone = model.Phone;
            currentAddress.ZipCode = model.ZipCode;
            currentAddress.IsDefault = model.IsDefault;
            currentAddress.Changed = DateTime.Now;

            ApplicationDbContext.Update(currentAddress);
            var result = await ApplicationDbContext.SaveAsync();
            if (!result.IsSuccess)
            {
                foreach (var resultError in result.Errors)
                {
                    resultModel.Errors.Add(new ErrorModel(resultError.Key, resultError.Message));
                }

                return Json(resultModel);
            }

            resultModel.IsSuccess = true;
            return Json(resultModel);
        }

        #endregion Partial Views

        [HttpPost]
        public virtual async Task<JsonResult> UploadUserPhoto(IFormFile file)
        {
            var resultModel = new ResultModel();
            if (file == null || file.Length == 0)
            {
                resultModel.IsSuccess = false;
                resultModel.Errors.Add(new ErrorModel { Key = string.Empty, Message = "Image not found" });
                return Json(resultModel);
            }

            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                resultModel.IsSuccess = false;
                resultModel.Errors.Add(new ErrorModel { Key = string.Empty, Message = "User not found" });
                return Json(resultModel);
            }

            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                currentUser.UserPhoto = memoryStream.ToArray();
            }

            var result = await UserManager.UpdateAsync(currentUser);
            if (result.Succeeded)
            {
                resultModel.IsSuccess = true;
                return Json(resultModel);
            }

            resultModel.IsSuccess = false;
            foreach (var error in result.Errors)
            {
                resultModel.Errors.Add(new ErrorModel { Key = error.Code, Message = error.Description });
            }

            return Json(resultModel);
        }

        protected virtual async Task<IEnumerable<SelectListItem>> GetCountrySelectList()
        {
            var countrySelectList = await ApplicationDbContext.Countries
                .AsNoTracking()
                .Select(x => new SelectListItem
                {
                    Text = x.Name,
                    Value = x.Id
                }).ToListAsync();

            countrySelectList.Insert(0, new SelectListItem(_localizer["system_select_country"], string.Empty));

            return countrySelectList;
        }

        /// <summary>
        /// Return roles select list items
        /// </summary>
        /// <returns></returns>
        protected virtual async Task<IEnumerable<SelectListItem>> GetRoleSelectListItemAsync()
        {
            var roles = await RoleManager.Roles
                .AsNoTracking()
                .Select(x => new SelectListItem
                {
                    Value = x.Id,
                    Text = x.Name
                }).ToListAsync();
            roles.Insert(0, new SelectListItem(_localizer["sel_role"], string.Empty));

            return roles;
        }

        /// <summary>
        /// Return Auth Group select list items
        /// </summary>
        /// <returns></returns>
        protected virtual async Task<IEnumerable<SelectListItem>> GetAuthGroupSelectListItemAsync()
        {
            var authGroups = await ApplicationDbContext.AuthGroups
                .AsNoTracking()
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = x.Name
                }).ToListAsync();
            authGroups.Insert(0, new SelectListItem(_localizer["sel_group"], string.Empty));

            return authGroups;
        }

        /// <summary>
        /// Return tenants select list items
        /// </summary>
        /// <returns></returns>
        protected virtual async Task<IEnumerable<SelectListItem>> GetTenantsSelectListItemAsync()
        {
            var tenants = await ApplicationDbContext.Tenants
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = x.Name
                }).ToListAsync();
            tenants.Insert(0, new SelectListItem("Select tenant", string.Empty));

            return tenants;
        }


        #region Api

        /// <summary>
        /// Get user by id
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        [Route("api/[controller]/[action]")]
        [HttpGet]
        [Produces("application/json", Type = typeof(ResultModel<UserProfileViewModel>))]
        public async Task<JsonResult> GetUserById([Required] Guid? userId)
        {

            if (userId == null)
                return Json(new InvalidParametersResultModel());

            var userRequest = await ApplicationDbContext.Users.FirstOrDefaultAsync(x => x.Id == userId.ToString());

            if (userRequest == null)
                return Json(new NotFoundResultModel());

            var user = userRequest.Adapt<UserProfileViewModel>();
            var userRoles = (await _userManager.GetUserRolesAsync(userRequest)).ToList();
            user.UserRoles = userRoles;
            user.UserRolesId = userRoles.Select(s => s.Id);

            if (user.JobPositionId != null)
            {
                var jobPositionRequest = await _jobPositionService.GetJobPositionByIdAsync(user.JobPositionId);
                if (jobPositionRequest.IsSuccess)
                    user.JobPosition = jobPositionRequest.Result;
            }


            return Json(new ResultModel
            {
                IsSuccess = true,
                Result = user
            });
        }

        /// <summary>
        /// Get paginated user 
        /// </summary>
        /// <returns></returns>
        [Route("api/[controller]/[action]")]
        [HttpPost]
        [Produces("application/json", Type = typeof(PagedResult<GearUser>))]
        public async Task<JsonResult> GetPaginatedUser(PageRequest request)
        {
            var query = ApplicationDbContext.Users
                .Where(x => !x.IsDeleted || request.IncludeDeleted);


            //request.PageRequestFilters = new List<PageRequestFilter>
            //{
            //    new PageRequestFilter{Propriety = "Role", Value = "ecf1ce53-5638-4d8b-94a2-586508a9b9ad"},
            //    new PageRequestFilter{Propriety = "Role", Value = "87b74ba9-ceea-4e8e-a433-dc10c9066f2d"}
            //};


            if (request.PageRequestFilters.Select(s => s.Propriety).Contains("Role"))
            {
                var listRoleId = request.PageRequestFilters
                    .Where(x => string.Equals(x.Propriety.Trim(), "Role".Trim(), StringComparison.CurrentCultureIgnoreCase))
                    .Select(s => s.Value.ToStringIgnoreNull().ToGuid())
                    .ToList();

                var listUsersId = new List<string>();
                var listRoles = await _userManager.FindRolesByIdAsync(listRoleId);

                foreach (var role in listRoles)
                {
                    var user = await _userManager.UserManager.GetUsersInRoleAsync(role.Name);
                    listUsersId.AddRange(user.Select(s => s.Id));
                }

                listUsersId = listUsersId.Distinct().ToList();
                query = query.Where(x => listUsersId.Contains(x.Id));

                request.PageRequestFilters = request.PageRequestFilters.Where(s => s.Propriety != "Role");
            }


            var result = await query.GetPagedAsync(request);


            var toReturn = new PagedResult<UserProfileViewModel>
            {
                CurrentPage = result.CurrentPage,
                IsSuccess = result.IsSuccess,
                Errors = result.Errors,
                KeyEntity = result.KeyEntity,
                PageCount = result.PageCount,
                PageSize = result.PageSize,
                RowCount = result.RowCount,
            };

            var listUser = new List<UserProfileViewModel>();

            foreach (var user in result.Result)
            {
                var userToAdd = user.Adapt<UserProfileViewModel>();
                userToAdd.UserRoles = (await _userManager.GetUserRolesAsync(user)).ToList();


                if (user.JobPositionId != null)
                {
                    var jobPositionRequest = await _jobPositionService.GetJobPositionByIdAsync(user.JobPositionId);
                    if (jobPositionRequest.IsSuccess)
                        userToAdd.JobPosition = jobPositionRequest.Result;
                }


                listUser.Add(userToAdd);
            }

            toReturn.Result = listUser;

            return Json(new ResultModel { IsSuccess = true, Result = toReturn });
        }


        /// <summary>
        /// Get paginated user 
        /// </summary>
        /// <returns></returns>
        [Route("api/[controller]/[action]")]
        [HttpGet]
        [Produces("application/json", Type = typeof(ResultModel<IEnumerable<UserProfileViewModel>>))]
        public async Task<JsonResult> GetUsers(bool includeDeleted = false)
        {
            var result = await ApplicationDbContext.Users
                .Where(x => !x.IsDeleted || includeDeleted).ToListAsync();

            var listUser = new List<UserProfileViewModel>();

            foreach (var user in result)
            {
                var userToAdd = user.Adapt<UserProfileViewModel>();
                userToAdd.UserRoles = (await _userManager.GetUserRolesAsync(user)).ToList();

                if (user.JobPositionId != null)
                {
                    var jobPositionRequest = await _jobPositionService.GetJobPositionByIdAsync(user.JobPositionId);
                    if (jobPositionRequest.IsSuccess)
                        userToAdd.JobPosition = jobPositionRequest.Result;
                }


                listUser.Add(userToAdd);
            }

            return Json(new SuccessResultModel<IEnumerable<UserProfileViewModel>>(listUser));
        }


        /// <summary>
        /// Deactivate user 
        /// </summary>
        /// <returns></returns>
        [Route("api/[controller]/[action]")]
        [HttpPost]
        [Produces("application/json", Type = typeof(ResultModel))]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserDelete)]
        public async Task<JsonResult> DeactivateUser([Required] Guid? userId)
        {
            if (userId == null)
                return Json(new InvalidParametersResultModel());

            if (IsCurrentUser(userId.ToString()))
            {
                return Json(new ResultModel { IsSuccess = false, Errors = new List<IErrorModel> { new ErrorModel { Message = "You can't delete current user" } } });
            }


            var user = await _userManager.UserManager.FindByIdAsync(userId.ToString());

            if (user == null)
                return Json(new InvalidParametersResultModel());


            user.IsDeleted = true;
            user.IsDisabled = true;
            var result = await UserManager.UpdateAsync(user);

            return Json(new ResultModel { IsSuccess = result.Succeeded });
        }

        /// <summary>
        /// Get paginated user 
        /// </summary>
        /// <returns></returns>
        [Route("api/[controller]/[action]")]
        [HttpGet]
        [Produces("application/json", Type = typeof(ResultModel))]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserDelete)]
        public async Task<JsonResult> ActivateUser([Required] Guid? userId)
        {
            if (userId == null)
                return Json(new InvalidParametersResultModel());


            var user = await _userManager.UserManager.FindByIdAsync(userId.ToString());

            if (user == null)
                return Json(new InvalidParametersResultModel());


            user.IsDeleted = false;
            user.IsDisabled = false;
            var result = await UserManager.UpdateAsync(user);

            return Json(new ResultModel { IsSuccess = result.Succeeded });
        }

        /// <summary>
        /// Get user paginated
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("api/[controller]/[action]")]
        [Produces("application/json", Type = typeof(ResultModel<>))]
        public async Task<JsonResult> GetCurrentUserInfo()
        {
            var userRequest = await _userManager.GetCurrentUserAsync();
            return Json(userRequest);
        }

        /// <summary>
        /// Add user 
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        [HttpPut]
        [Route("api/[controller]/[action]")]
        [Produces("application/json", Type = typeof(ResultModel<Guid>))]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserCreate)]
        public async Task<JsonResult> AddUser(CreateUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.Roles = await GetRoleSelectListItemAsync();
                model.Groups = await GetAuthGroupSelectListItemAsync();
                model.Tenants = await GetTenantsSelectListItemAsync();
                model.CountrySelectListItems = await GetCountrySelectList();
                return JsonModelStateErrors();
            }

            var user = new GearUser
            {
                UserName = model.UserName,
                Email = model.Email,
                Created = DateTime.Now,
                Changed = DateTime.Now,
                IsDeleted = model.IsDeleted,
                Author = User.Identity.Name,
                AuthenticationType = model.AuthenticationType,
                IsEditable = true,
                TenantId = model.TenantId,
                LastPasswordChanged = DateTime.Now,
                UserFirstName = model.FirstName,
                UserLastName = model.LastName,
                Birthday = model.Birthday ?? DateTime.MinValue,
                AboutMe = model.AboutMe,
                JobPositionId = model.JobPositionId,
                PhoneNumber = model.PhoneNumber,
                DialCode = model.DialCode
            };


            if (model.UserPhoto != null)
            {
                using (var memoryStream = new MemoryStream())
                {
                    await model.UserPhoto.CopyToAsync(memoryStream);
                    user.UserPhoto = memoryStream.ToArray();
                }
            }


            var result = await UserManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                model.Roles = await GetRoleSelectListItemAsync();
                model.Groups = await GetAuthGroupSelectListItemAsync();
                model.Tenants = await GetTenantsSelectListItemAsync();
                model.CountrySelectListItems = await GetCountrySelectList();
                return JsonModelStateErrors();
            }


            Logger.LogInformation("User {0} created successfully", user.UserName);

            if (model.SelectedRoleId != null && model.SelectedRoleId.Any())
            {
                var rolesNameList = await RoleManager.Roles.Where(x => model.SelectedRoleId.Contains(x.Id))
                    .Select(x => x.Name).ToListAsync();
                var roleAddResult = await UserManager.AddToRolesAsync(user, rolesNameList);
                if (!roleAddResult.Succeeded)
                {
                    foreach (var error in roleAddResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }

                    model.Roles = await GetRoleSelectListItemAsync();
                    model.Groups = await GetAuthGroupSelectListItemAsync();
                    model.Tenants = await GetTenantsSelectListItemAsync();
                    model.CountrySelectListItems = await GetCountrySelectList();
                    return JsonModelStateErrors();
                }
            }

            if (model.SelectedGroupId != null && model.SelectedGroupId.Any())
            {
                var userGroupList = model.SelectedGroupId
                    .Select(_ => new UserGroup { AuthGroupId = Guid.Parse(_), UserId = user.Id }).ToList();

                await ApplicationDbContext.UserGroups.AddRangeAsync(userGroupList);
            }
            else
            {
                var groupId = await ApplicationDbContext.AuthGroups.FirstOrDefaultAsync();
                if (groupId != null)
                {
                    ApplicationDbContext.UserGroups.Add(new UserGroup
                    {
                        AuthGroupId = groupId.Id,
                        UserId = user.Id.ToString()
                    });
                }
            }

            var dbResult = await ApplicationDbContext.SaveAsync();
            if (!dbResult.IsSuccess)
            {
                ModelState.AppendResultModelErrors(dbResult.Errors);
                model.Roles = await GetRoleSelectListItemAsync();
                model.Groups = await GetAuthGroupSelectListItemAsync();
                model.Tenants = await GetTenantsSelectListItemAsync();
                model.CountrySelectListItems = await GetCountrySelectList();
                return JsonModelStateErrors();
            }

            IdentityEvents.Users.UserCreated(new UserCreatedEventArgs
            {
                Email = user.Email,
                UserName = user.UserName,
                UserId = user.Id.ToGuid()
            });


            return Json(new ResultModel
            {
                IsSuccess = true,
                Result = user.Id
            });
        }

        [HttpGet]
        [Route("api/[controller]/[action]")]
        [Produces("application/json", Type = typeof(ResultModel))]
        public JsonResult GetUsersFromLdap()
        {
            var users = _ldapService.GetAllUsers();
            return Json(new SuccessResultModel<IEnumerable<GetLdapUsersViewModel>>(_mapper.Map<IEnumerable<GetLdapUsersViewModel>>(users)));
        }

        /// <summary>
        /// Add user 
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("api/[controller]/[action]")]
        [Produces("application/json", Type = typeof(ResultModel))]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserUpdate)]
        public virtual async Task<JsonResult> UpdateUser([Required] string id, UpdateUserViewModel model)
        {
            if (Guid.Parse(id) != model.Id)
            {
                return Json(new NotFoundResultModel());
            }

            var applicationUser = await ApplicationDbContext.Users.SingleOrDefaultAsync(m => m.Id == id.ToString());
            var roles = RoleManager.Roles.AsEnumerable();
            var groupsList = ApplicationDbContext.AuthGroups.AsEnumerable();
            var userGroupListList = ApplicationDbContext.UserGroups.Where(x => x.UserId == applicationUser.Id).ToList()
                .Select(s => s.AuthGroupId.ToString()).ToList();
            var userRolesNames = await UserManager.GetRolesAsync(applicationUser);
            var userRoleList = userRolesNames
                .Select(item => roles.FirstOrDefault(x => x.Name == item)?.Id.ToString())
                .ToList();

            model.Roles = roles;
            model.Groups = groupsList;
            model.SelectedGroupId = userGroupListList;
            model.Tenants = ApplicationDbContext.Tenants.Where(x => !x.IsDeleted).ToList();

            if (!ModelState.IsValid)
            {
                model.SelectedRoleId = userRoleList;
                foreach (var error in ViewData.ModelState.Values.SelectMany(stateValue => stateValue.Errors))
                {
                    ModelState.AddModelError(string.Empty, error.ErrorMessage);
                }

                return JsonModelStateErrors();
            }

            // Update User Data
            var user = await UserManager.FindByIdAsync(id);
            if (user == null)
            {
                return Json(new NotFoundResultModel());
            }

            if (model.AuthenticationType.Equals(AuthenticationType.Ad))
            {
                var ldapUser = await _ldapUserManager.FindByNameAsync(model.UserName);
                if (ldapUser == null)
                {
                    model.SelectedRoleId = userRoleList;
                    ModelState.AddModelError("", $"There is no AD user with this username : {model.UserName}");
                    return JsonModelStateErrors();
                }

                user.UserName = ldapUser.SamAccountName;
                user.Email = ldapUser.EmailAddress;
            }

            user.IsDeleted = model.IsDeleted;
            user.Changed = DateTime.Now;
            user.Email = model.Email;
            user.ModifiedBy = User.Identity.Name;
            user.UserName = model.UserName;
            user.TenantId = model.TenantId;
            user.UserFirstName = model.FirstName;
            user.UserLastName = model.LastName;
            user.JobPositionId = model.JobPositionId;
            user.PhoneNumber = model.PhoneNumber;
            user.DialCode = model.DialCode;
            if (model.UserPhotoUpdateFile != null)
            {
                using (var memoryStream = new MemoryStream())
                {
                    await model.UserPhotoUpdateFile.CopyToAsync(memoryStream);
                    user.UserPhoto = memoryStream.ToArray();
                }
            }

            var result = await UserManager.UpdateAsync(user);
            if (model.Password != null)
            {
                var token = await UserManager.GeneratePasswordResetTokenAsync(user);
                var changeResult = await UserManager.ResetPasswordAsync(user, token, model.Password);
                if (!changeResult.Succeeded)
                {
                    model.SelectedRoleId = userRoleList;
                    foreach (var _ in changeResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, _.Description);
                    }

                    return JsonModelStateErrors();
                }
            }

            if (!result.Succeeded)
            {
                model.SelectedRoleId = userRoleList;
                foreach (var _ in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, _.Description);
                }

                return JsonModelStateErrors();
            }

            // REFRESH USER ROLES
            var userRoles = await ApplicationDbContext.UserRoles.Where(x => x.UserId == user.Id).ToListAsync();
            var rolesList = new List<string>();
            foreach (var _ in userRoles)
            {
                var role = await RoleManager.FindByIdAsync(_.RoleId);
                rolesList.Add(role.Name);
            }

            await UserManager.RemoveFromRolesAsync(user, rolesList);

            var roleNameList = new List<string>();
            foreach (var _ in model.SelectedRoleId)
            {
                var role = await RoleManager.FindByIdAsync(_);
                roleNameList.Add(role.Name);
            }

            await UserManager.AddToRolesAsync(user, roleNameList);

            if (model.Groups != null && model.Groups.Any())
            {
                //Refresh groups
                var currentGroupsList =
                    await ApplicationDbContext.UserGroups.Where(x => x.UserId == user.Id).ToListAsync();
                ApplicationDbContext.UserGroups.RemoveRange(currentGroupsList);

                var userGroupList = model.SelectedGroupId
                    .Select(groupId => new UserGroup { UserId = user.Id, AuthGroupId = Guid.Parse(groupId) }).ToList();
                await ApplicationDbContext.UserGroups.AddRangeAsync(userGroupList);
            }

            await UserManager.UpdateSecurityStampAsync(user);

            //Refresh user claims for this user
            //await user.RefreshClaims(Context, signInManager);
            IdentityEvents.Users.UserUpdated(new UserUpdatedEventArgs
            {
                Email = user.Email,
                UserName = user.UserName,
                UserId = user.Id.ToGuid()
            });
            return Json(new ResultModel { IsSuccess = true });
        }


        /// <summary>
        /// Delete user form DB
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete]
        [Route("api/[controller]/[action]")]
        [Produces("application/json", Type = typeof(ResultModel))]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserDelete)]
        public async Task<JsonResult> DeleteUser([Required] Guid? id)
        {
            if (id == null)
            {
                return Json(new InvalidParametersResultModel());
            }

            if (IsCurrentUser(id.ToString()))
            {
                return Json(new ResultModel { IsSuccess = false, Errors = new List<IErrorModel> { new ErrorModel { Message = "You can't delete current user" } } });
            }

            var applicationUser = await ApplicationDbContext.Users.SingleOrDefaultAsync(m => m.Id == id.ToString());
            if (applicationUser == null)
            {
                return Json(new ResultModel { IsSuccess = false, Errors = new List<IErrorModel> { new ErrorModel { Message = "User not found" } } });
            }

            if (applicationUser.IsEditable == false)
            {
                return Json(new ResultModel { IsSuccess = false, Errors = new List<IErrorModel> { new ErrorModel { Message = "Is system user!!!" } } });
            }

            try
            {
                // await UserManager.UpdateSecurityStampAsync(applicationUser);
                await UserManager.DeleteAsync(applicationUser);
                IdentityEvents.Users.UserDelete(new UserDeleteEventArgs
                {
                    Email = applicationUser.Email,
                    UserName = applicationUser.UserName,
                    UserId = applicationUser.Id.ToGuid()
                });
                return Json(new ResultModel { IsSuccess = true });
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message);
                return Json(new ResultModel { IsSuccess = false, Errors = new List<IErrorModel> { new ErrorModel { Message = "Error on delete!!!" } } });
            }
        }

        /// <summary>
        /// Invite new user
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("api/[controller]/[action]")]
        [Produces("application/json", Type = typeof(ResultModel))]
        [AuthorizePermission(PermissionsConstants.CorePermissions.BpmUserCreate)]
        public async Task<JsonResult> InviteNewUserAsync(InviteNewUserViewModel model)
        {
            var resultModel = new ResultModel();
            if (!ModelState.IsValid)
            {
                resultModel.AttachModelState(ModelState);
                return Json(resultModel);
            }

            resultModel = await _organizationService.InviteNewUserByEmailAsync(model);

            return Json(resultModel);
        }


        /// <summary>
        /// ChangePassword
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("api/[controller]/[action]")]
        [Produces("application/json", Type = typeof(ResultModel))]
        public virtual async Task<JsonResult> ChangePassword([Required] ChangeUserPasswordViewModel model)
        {


            var user = await UserManager.FindByIdAsync(model.UserId.ToString());
            if (user == null)
            {
                ModelState.AddModelError("", "The user is no longer in the system");
                return JsonModelStateErrors();
            }

            if (!await UserManager.CheckPasswordAsync(user, model.CurrentPassword))
            {
                ModelState.AddModelError("Current Password", "Incorrect current password.");
                return JsonModelStateErrors();
            }

            var hasher = new PasswordHasher<GearUser>();
            var hashedPassword = hasher.HashPassword(user, model.Password);
            user.PasswordHash = hashedPassword;
            user.LastPasswordChanged = DateTime.Now;
            var result = await UserManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                IdentityEvents.Users.UserPasswordChange(new UserChangePasswordEventArgs
                {
                    Email = user.Email,
                    UserName = user.UserName,
                    UserId = user.Id.ToGuid(),
                    Password = model.Password
                });
                return Json(new ResultModel { IsSuccess = true });
            }

            foreach (var _ in result.Errors)
            {
                ModelState.AddModelError(string.Empty, _.Description);
            }

            return JsonModelStateErrors();
        }

        /// <summary>
        /// Update user profile info
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("api/[controller]/[action]")]
        [Produces("application/json", Type = typeof(ResultModel))]

        public virtual async Task<JsonResult> UpdateAccountInformation(UserProfileEditViewModel model)
        {
            var resultModel = new ResultModel();
            if (!ModelState.IsValid)
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "Invalid model"));
                return Json(resultModel);
            }

            var currentUser = await UserManager.Users.FirstOrDefaultAsync(x => x.Id.Equals(model.Id));
            if (currentUser == null)
            {
                resultModel.Errors.Add(new ErrorModel(string.Empty, "User not found!"));
                return Json(resultModel);
            }

            currentUser.UserFirstName = model.UserFirstName;
            currentUser.UserLastName = model.UserLastName;
            currentUser.Birthday = model.Birthday;
            currentUser.AboutMe = model.AboutMe;
            currentUser.PhoneNumber = model.UserPhoneNumber;
            currentUser.Email = model.Email;

            var result = await UserManager.UpdateAsync(currentUser);
            if (result.Succeeded)
            {
                resultModel.IsSuccess = true;
                return Json(resultModel);
            }

            foreach (var identityError in result.Errors)
            {
                resultModel.Errors.Add(new ErrorModel(identityError.Code, identityError.Description));
            }

            return Json(resultModel);
        }

        #endregion
    }
}
