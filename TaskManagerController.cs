using Microsoft.AspNetCore.Mvc;
using GR.Core.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;
using GR.Core.BaseControllers;
using GR.Core.Extensions;
using GR.Core.Helpers.Pagination;
using GR.Core.Helpers.Responses;
using GR.Identity.Abstractions;
using GR.TaskManager.Abstractions;
using GR.TaskManager.Abstractions.Enums;
using GR.TaskManager.Abstractions.Helpers;
using GR.TaskManager.Abstractions.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;

namespace GR.TaskManager.Razor.Controllers
{
    [Authorize]
    public sealed class TaskManagerController : BaseGearController
    {

        #region Injectable

        /// <summary>
        /// Inject Task service
        /// </summary>
        private readonly ITaskManager _taskManager;

        /// <summary>
        /// Inject user manager
        /// </summary>
        private readonly IUserManager<GearUser> _userManager;

        #endregion

        public TaskManagerController(ITaskManager taskManager, IUserManager<GearUser> userManager)
        {
            _taskManager = taskManager;
            _userManager = userManager;
        }

        /// <summary>
        /// Index page
        /// </summary>
        /// <returns></returns>
        [HttpGet("/TaskManager")]
        public IActionResult Index(int nr = 0)
        {
            ViewBag.nr = 0;
            return View(new List<string>());
        }

        public async Task<IActionResult> Details(Guid id)
        {
            var taskRequest = await _taskManager.GetTaskAsync(id);
            if (!taskRequest.IsSuccess) return NotFound();
            return View(taskRequest.Result);
        }

        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> Tasks(int nr = 0)
        {
            return View(nr);
        }

        [HttpGet]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<SelectList>))]
        public JsonResult GetTaskPriorityList()
        {
            var directions = from TaskPriority d in Enum.GetValues(typeof(TaskPriority))
                select new {ID = (int) d, Name = d.ToString()};
            return Json(new SelectList(directions, "ID", "Name", 0));
        }

        [HttpGet]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<SelectList>))]
        public JsonResult GetTaskStatusList()
        {
            var directions = from Abstractions.Enums.TaskStatus d in Enum.GetValues(typeof(Abstractions.Enums.TaskStatus))
                select new {ID = (int) d, Name = d.ToString()};
            return Json(new SelectList(directions, "ID", "Name", 0));
        }

        [HttpGet]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<SelectList>))]
        public JsonResult GetUsersList(bool includeDeleted = false)
        {
            var users = _userManager.UserManager.Users.Where(x => x.TenantId == _userManager.CurrentUserTenantId && (!x.IsDeleted || includeDeleted)).ToList();

            var directions = from GearUser d in users select new {ID = d.Id, Name = d.UserName};
            return Json(new SelectList(directions, "ID", "Name", 0));
        }



        [HttpGet]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<IEnumerable<GearUser>>))]
        public JsonResult GetUsers(bool includeDeleted = false)
        {
            var users = _userManager.UserManager.Users.Where(x => x.TenantId == _userManager.CurrentUserTenantId && (!x.IsDeleted || includeDeleted)).ToList();
           
            return Json(new SuccessResultModel<IEnumerable<GearUser>>(users));
        }

        [HttpGet]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<GetTaskViewModel>))]
        public async Task<JsonResult> GetTask(Guid id)
        {
            if (id == Guid.Empty) return Json(ExceptionMessagesEnum.NullParameter.ToErrorModel());

            var response = await _taskManager.GetTaskAsync(id);
            return Json(response, SerializerSettings);
        }

        [HttpGet]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<List<GetTaskViewModel>>))]
        public async Task<JsonResult> GetUserTasks(PageRequest request)
        {
            var userName = HttpContext.User.Identity.Name;

            var response = await _taskManager.GetUserTasksAsync(userName, request);
            return Json(response, SerializerSettings);
        }

        [HttpGet]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<PagedResult<GetTaskViewModel>>))]
        public async Task<JsonResult> GetAssignedTasks(PageRequest request)
        {
            var user = await _userManager.GetCurrentUserAsync();

            if (user.Result == null) return Json(ExceptionMessagesEnum.UserNotFound.ToErrorModel());

            var response = await _taskManager.GetAssignedTasksAsync(user.Result.Id.ToGuid(), user.Result.UserName, request);
            return Json(response, SerializerSettings);
        }

        [HttpPost]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<PagedResult<GetTaskViewModel>>))]
        public async Task<JsonResult> GetAllUsersTasks(PageRequest request)
        {
            var user = await _userManager.GetCurrentUserAsync();

            if (user.Result == null) return Json(ExceptionMessagesEnum.UserNotFound.ToErrorModel());

            var response = await _taskManager.GetAllUserTasksAsync(user.Result.Id.ToGuid(), user.Result.UserName, request);
            return Json(response, SerializerSettings);
        }


        [HttpPost]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<PagedResult<GetTaskViewModel>>))]
        public async Task<JsonResult> GetAllTasks(PageRequest request)
        {
            var response = await _taskManager.GetAllTasksAsync(request);
            return Json(response, SerializerSettings);
        }


        [HttpGet]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<PagedResult<GetTaskViewModel>>))]
        public async Task<JsonResult> GetTaskByLeadId([Required]Guid leadId,PageRequest request)
        {
            var response = await _taskManager.GetTaskByLeadIdAsync(leadId,  request);
            return Json(response, SerializerSettings);
        }

        [HttpGet]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<PagedResult<GetTaskViewModel>>))]
        public async Task<JsonResult> GetTaskByOrganizationId([Required]Guid organizationId,PageRequest request)
        {
            var response = await _taskManager.GetAllTasksByOrganizationIdPaginatedAsync(organizationId,  request);
            return Json(response, SerializerSettings);
        }

        [HttpGet]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<PagedResult<GetTaskItemViewModel>>))]
        public async Task<JsonResult> GetTaskItems(Guid id)
        {
            if (id == Guid.Empty) return Json(ExceptionMessagesEnum.NullParameter.ToErrorModel());

            var response = await _taskManager.GetTaskItemsAsync(id);
            return Json(response);
        }

        [HttpPost]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<CreateTaskViewModel>))]
        public async Task<JsonResult> CreateTask(CreateTaskViewModel model)
        {
            if (!ModelState.IsValid) return Json(new InvalidParametersResultModel().AttachModelState(ModelState));

            var response = await _taskManager.CreateTaskAsync(model, Url);
            return Json(response);
        }

        [HttpPost]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<UpdateTaskViewModel>))]
        public async Task<JsonResult> UpdateTask(UpdateTaskViewModel model)
        {
            if (!ModelState.IsValid) return JsonModelStateErrors();

            var response = await _taskManager.UpdateTaskAsync(model, Url);
            return Json(response);
        }

        [HttpPost]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel))]
        public async Task<JsonResult> DeleteTask(Guid id)
        {
            if (id == Guid.Empty) return Json(ExceptionMessagesEnum.NullParameter.ToErrorModel());

            var response = await _taskManager.DeleteTaskAsync(id);
            return Json(response);
        }

        [HttpPost]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel))]
        public async Task<JsonResult> DeleteTaskPermanent(Guid id)
        {
            if (id == Guid.Empty) return Json(ExceptionMessagesEnum.NullParameter.ToErrorModel());

            var response = await _taskManager.DeletePermanentTaskAsync(id);
            return Json(response);
        }

        [HttpPost]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel))]
        public async Task<JsonResult> RestoreTask(Guid id)
        {
            if (id == Guid.Empty) return Json(ExceptionMessagesEnum.NullParameter.ToErrorModel());

            var response = await _taskManager.RestoreTaskAsync(id);
            return Json(response);
        }

        [HttpPost]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<CreateTaskItemViewModel>))]
        public async Task<JsonResult> CreateTaskItem(CreateTaskItemViewModel model)
        {
            if (!ModelState.IsValid) return Json(ModelState.ToErrorModel<CreateTaskItemViewModel>());

            var response = await _taskManager.CreateTaskItemAsync(model);
            return Json(response);
        }

        [HttpPost]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel<UpdateTaskItemViewModel>))]
        public async Task<JsonResult> UpdateTaskItem(UpdateTaskItemViewModel model)
        {
            if (!ModelState.IsValid) return JsonModelStateErrors();

            var response = await _taskManager.UpdateTaskItemAsync(model);
            return Json(response);
        }

        [HttpPost]
        [Route(DefaultApiRouteTemplate)]
        [Produces("application/json", Type = typeof(ResultModel))]
        public async Task<JsonResult> DeleteTaskItem(Guid id)
        {
            if (id == Guid.Empty) return Json(ExceptionMessagesEnum.NullParameter.ToErrorModel());

            var response = await _taskManager.DeleteTaskItemAsync(id);
            return Json(response);
        }
    }
}
