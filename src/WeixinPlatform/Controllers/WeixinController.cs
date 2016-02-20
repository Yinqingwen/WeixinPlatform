using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Mvc;
using Weixin.MP;
using Weixin.MP.MvcExtension;
using Weixin.MP.Entities.Request;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Weixin.MP.CommonService.CustomMessageHandler;
using System.IO;

namespace WinxinPlatform.Controllers
{
    public class WeixinController : Controller
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

        readonly Func<string> _getRandomFileName = () => DateTime.Now.Ticks + Guid.NewGuid().ToString("n").Substring(0, 6);

        /// <summary>
        /// 配置文件读取
        /// </summary>
        public IConfigurationRoot Configuration { get; set; }

        public WeixinController(IHostingEnvironment env)
        {
            //配置文件源
            var builder = new ConfigurationBuilder()
                .AddJsonFile(@"Weixinsettings.json")
                .AddJsonFile(@"Weixinsettings.{env.EnvironmentName}.json", optional: true);

            if (env.IsDevelopment())
            {
                builder.AddUserSecrets();
                builder.AddApplicationInsightsSettings(developerMode: true);
            }

            builder.AddEnvironmentVariables();
            Configuration = builder.Build();

            //获取相关信息
            Token = Configuration.GetSection("WeixinToken").Value;
            EncodingAESKey = Configuration.GetSection("WeixinEncodingAESKey").Value;
            AppId = Configuration.GetSection("WeixinAppId").Value;
        }

        /// <summary>
        /// 微信后台验证地址（使用Get），微信后台的“接口配置信息”的Url填写如：http://AppDomain/weixin
        /// </summary>
        [HttpGet]
        public IActionResult Index(PostModel postModel, string echostr)
        {
            if (CheckSignature.Check(postModel.Signature, postModel.Timestamp, postModel.Nonce, Token))
                return Content(echostr);
            else
                return Content("failed:" + postModel.Signature + "," + Weixin.MP.CheckSignature.GetSignature(postModel.Timestamp, postModel.Nonce, Token) + "。" + "如果你在浏览器中看到这句话，说明此地址可以被作为微信公众账号后台的Url，请注意保持Token一致。");
        }

        /// <summary>
        /// 用户发送消息后，微信平台自动Post一个请求到这里，并等待响应XML。
        /// PS：此方法为简化方法，效果与OldPost一致。
        /// v0.8之后的版本可以结合Weixin.MP.MvcExtension扩展包，使用WeixinResult，见MiniPost方法。
        /// </summary>
        /// <param name="postModel"></param>
        /// <returns></returns>
        [HttpPost]
        [ActionName("Index")]
        public IActionResult Post(PostModel postModel)
        {
            if (!CheckSignature.Check(postModel.Signature, postModel.Timestamp, postModel.Nonce, Token))
                return Content("参数错误！");

            //根据自己后台的设置保持一致
            postModel.Token = Token;
            //根据自己后台的设置保持一致
            postModel.EncodingAESKey = EncodingAESKey;
            //根据自己后台的设置保持一致
            postModel.AppId = AppId;

            //v4.2.2之后的版本，可以设置每个人上下文消息储存的最大数量，防止内存占用过多，如果该参数小于等于0，则不限制
            var maxRecordCount = 10;

            var logPath = System.Web.HttpContext.Current.Server.MapPath(string.Format("~/App_Data/MP/{0}/", DateTime.Now.ToString("yyyy-MM-dd")));
            if (!Directory.Exists(logPath))
            {
                Directory.CreateDirectory(logPath);
            }

            //自定义MessageHandler，对微信请求的详细判断操作都在这里面。
            var messageHandler = new CustomMessageHandler(Request.Body, postModel, maxRecordCount);

            try
            {
                //测试时可开启此记录，帮助跟踪数据，使用前请确保App_Data文件夹存在，且有读写权限。
                messageHandler.RequestDocument.Save(Path.Combine(logPath, string.Format("{0}_Request_{1}.txt", _getRandomFileName(), messageHandler.RequestMessage.FromUserName)));
                if (messageHandler.UsingEcryptMessage)
                {
                    messageHandler.EcryptRequestDocument.Save(Path.Combine(logPath, string.Format("{0}_Request_Ecrypt_{1}.txt", _getRandomFileName(), messageHandler.RequestMessage.FromUserName)));
                }

                /* 如果需要添加消息去重功能，只需打开OmitRepeatedMessage功能，SDK会自动处理。
                 * 收到重复消息通常是因为微信服务器没有及时收到响应，会持续发送2-5条不等的相同内容的RequestMessage*/
                messageHandler.OmitRepeatedMessage = true;

                //执行微信处理过程
                messageHandler.Execute();

                //测试时可开启，帮助跟踪数据

                if (messageHandler.ResponseDocument != null)
                {
                    messageHandler.ResponseDocument.Save(Path.Combine(logPath, string.Format("{0}_Response_{1}.txt", _getRandomFileName(), messageHandler.RequestMessage.FromUserName)));
                }

                if (messageHandler.UsingEcryptMessage)
                {
                    //记录加密后的响应信息
                    messageHandler.FinalResponseDocument.Save(Path.Combine(logPath, string.Format("{0}_Response_Final_{1}.txt", _getRandomFileName(), messageHandler.RequestMessage.FromUserName)));
                }

                //为了解决官方微信5.0软件换行bug暂时添加的方法，平时用下面一个方法即可
                return new FixWeixinBugWeixinResult(messageHandler);
            }
            catch (Exception ex)
            {
                using (TextWriter tw = new StreamWriter(System.Web.HttpContext.Current.Server.MapPath("~/App_Data/Error_" + _getRandomFileName() + ".txt")))
                {
                    tw.WriteLine("ExecptionMessage:" + ex.Message);
                    tw.WriteLine(ex.Source);
                    tw.WriteLine(ex.StackTrace);

                    if (messageHandler.ResponseDocument != null)
                    {
                        tw.WriteLine(messageHandler.ResponseDocument.ToString());
                    }

                    if (ex.InnerException != null)
                    {
                        tw.WriteLine("========= InnerException =========");
                        tw.WriteLine(ex.InnerException.Message);
                        tw.WriteLine(ex.InnerException.Source);
                        tw.WriteLine(ex.InnerException.StackTrace);
                    }

                    tw.Flush();
                    tw.Close();
                }
                return Content("");
            }
        }
    }
}
