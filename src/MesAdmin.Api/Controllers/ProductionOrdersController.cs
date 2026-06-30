using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MesAdmin.Application.Security;

namespace MesAdmin.Api.Controllers;

/// <summary>
/// 工单 API（示例：体现关键权限点）。
/// 对应 PRD：物料防错错误需质量工程师解锁、完工确认需质量工程师审核放行。
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize] // 全局要求认证
public class ProductionOrdersController : ControllerBase
{
    /// <summary>查询工单列表（所有已认证角色可访问）</summary>
    [HttpGet]
    public IActionResult List()
    {
        return Ok(new { message = "工单列表", user = User.Identity?.Name });
    }

    /// <summary>
    /// 完工确认 — 需质量工程师审核放行。
    /// PRD M01：31 工序完成，质量工程师审核放行，状态变 Completed。
    /// </summary>
    [HttpPost("{id}/complete")]
    [Authorize(Roles = MesRoles.QualityEngineer)]
    public IActionResult Complete(string id)
    {
        return Ok(new { message = $"工单 {id} 完工确认已放行", approvedBy = User.Identity?.Name });
    }

    /// <summary>
    /// 物料防错解锁 — 需质量工程师。
    /// PRD M02：物料防错错误锁定设备，必须质量工程师解锁。
    /// </summary>
    [HttpPost("{id}/unlock-equipment")]
    [Authorize(Roles = MesRoles.QualityEngineer)]
    public IActionResult UnlockEquipment(string id)
    {
        return Ok(new { message = $"工单 {id} 设备已解锁", unlockedBy = User.Identity?.Name });
    }

    /// <summary>
    /// 开工 — 需班组长。
    /// PRD M01：班组长扫码触发 Cleipnir Saga 启动 31 道工序。
    /// </summary>
    [HttpPost("{id}/start")]
    [Authorize(Roles = $"{MesRoles.ShiftLeader},{MesRoles.ProductionManager}")]
    public IActionResult Start(string id)
    {
        return Ok(new { message = $"工单 {id} 已开工", startedBy = User.Identity?.Name });
    }
}
