using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Mvc;
using Microsoft.Extensions.Configuration;
using Weixin.MP;
using Weixin.MP.Entities;
using Weixin.MP.Entities.Menu;
using Weixin.MP.CommonAPIs;
using Weixin.Entities;

namespace WinxinPlatform.Controllers
{
    public class MenuController : Controller
    {
        /// <summary>
        /// 与微信公众账号后台的Token设置保持一致，区分大小写。
        /// </summary>
        public static string Token = string.Empty;

        /// <summary>
        /// 与微信公众账号后台的EncodingAESKey设置保持一致，区分大小写。
        /// </summary>
        public static string EncodingAESKey = string.Empty;

        /// <summary>
        /// 与微信公众账号后台的AppId设置保持一致，区分大小写。
        /// </summary>
        public static string AppId = string.Empty;

        /// <summary>
        /// 配置文件读取
        /// </summary>
        public IConfigurationRoot Configuration { get; set; }

        public MenuController()
        {
            //配置文件源
            var builder = new ConfigurationBuilder()
                .AddJsonFile(@"Weixinsettings.json");

            Configuration = builder.Build();
            //获取相关信息
            Token = Configuration.GetSection("WeixinToken").Value;
            EncodingAESKey = Configuration.GetSection("WeixinEncodingAESKey").Value;
            AppId = Configuration.GetSection("WeixinAppId").Value;
        }

        // GET: /<controller>/
        public IActionResult Index()
        {
            GetMenuResult result = new GetMenuResult(new ButtonGroup());
            //初始化
            for (int i = 0; i < 3; i++)
            {
                var subButton = new SubButton();
                for (int j = 0; j < 5; j++)
                {
                    var singleButton = new SingleClickButton();
                    subButton.sub_button.Add(singleButton);
                }
            }
            return View(result);
        }

        public IActionResult GetToken(string appId, string appSecret)
        {
            try
            {
                var result = CommonApi.GetToken(appId, appSecret);
                return Json(result);
            }
            catch(Exception)
            {
                //TODO:为简化代码，这里不处理异常（如Token过期）
                return Json(new { error = "执行过程发生错误！" });
            }
        }

        public IActionResult GetMenu(string token)
        {
            var result = CommonApi.GetMenu(token);
            if (result == null)
                return Json(new { error = "菜单不存在或验证失败！" });
            return Json(result);
        }

        public IActionResult DeleteMenu(string token)
        {
            try
            {
                var result = CommonApi.DeleteMenu(token);
                var json = new
                {
                    Success = result.errmsg == "ok",
                    Message = result.errmsg
                };
                return Json(json);
            }
            catch (Exception ex)
            {
                var json = new { Success = false, Message = ex.Message };
                return Json(json);
            }
        }

        [HttpPost]
        public IActionResult CreateMenu(string token, GetMenuResultFull resultFull, MenuMatchRule menuMatchRule)
        {
            var useAddCondidionalApi = menuMatchRule != null && !menuMatchRule.CheckAllNull();
            var apiName = string.Format("使用接口：{0}。", (useAddCondidionalApi ? "个性化菜单接口" : "普通自定义菜单接口"));
            try
            {
                //重新整理按钮信息
                WxJsonResult result = null;
                IButtonGroupBase buttonGroup = null;
                if (useAddCondidionalApi)
                {
                    //个性化接口
                    buttonGroup = CommonApi.GetMenuFromJsonResult(resultFull, new ConditionalButtonGroup()).menu;

                    var addConditionalButtonGroup = buttonGroup as ConditionalButtonGroup;
                    addConditionalButtonGroup.matchrule = menuMatchRule;
                    result = CommonApi.CreateMenuConditional(token, addConditionalButtonGroup);
                    apiName += string.Format("menuid：{0}。", (result as CreateMenuConditionalResult).menuid);
                }
                else
                {
                    //普通接口
                    buttonGroup = CommonApi.GetMenuFromJsonResult(resultFull, new ButtonGroup()).menu;
                    result = CommonApi.CreateMenu(token, buttonGroup);
                }

                var json = new
                {
                    Success = result.errmsg == "ok",
                    Message = "菜单更新成功。" + apiName
                };

                return Json(json);
            }
            catch(Exception ex)
            {
                var json = new { Success = false, Message = string.Format("更新失败：{0}。{1}", ex.Message, apiName) };
                return Json(json);
            }
        }
    }
}
