﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.Owin.Security;
using ContosoWebApplication.Models;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.IO;


namespace ContosoWebApplication.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        string ApimProxyEndpoint = "<your apim proxy endpoint>";
        string ApimDevPortalSignoutUrl = "https://<your dev portal>/signout";
        string ApimDevPortalDevUrl = "https://<your dev portal>/developer";
        string ApimDelegationKey = "<your generated delegation key>";


        public AccountController()
            : this(new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(new ApplicationDbContext())))
        {
        }

        public AccountController(UserManager<ApplicationUser> userManager)
        {
            UserManager = userManager;
        }

        public UserManager<ApplicationUser> UserManager { get; private set; }

        //
        // GET: /Account/Login
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        //
        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model, string returnUrl)
        {
            if (ModelState.IsValid)
            {
                var user = await UserManager.FindAsync(model.UserName, model.Password);
                if (user != null)
                {
                    await SignInAsync(user, model.RememberMe);
                    return RedirectToLocal(returnUrl);
                }
                else
                {
                    ModelState.AddModelError("", "Invalid username or password.");
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        //
        // GET: /Account/Register
        [AllowAnonymous]
        public ActionResult Register()
        {
            return View();
        }

        public static string SerializeToJson<TAnything>(TAnything value)
        {
            return JsonConvert.SerializeObject(value, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }


        public static TAnything DeserializeToJson<TAnything>(String value)
        {
            var settings = new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Utc };
            var reader = new StringReader(value);

            var serializer = JsonSerializer.Create(settings);
            var jsonReader = new JsonTextReader(reader);

            return (TAnything)serializer.Deserialize(jsonReader, typeof(TAnything));
        }

        //
        // POST: /Account/Register
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser() { UserName = model.UserName };
                var result = await UserManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    //create user in APIM as well
                    using (var client = new HttpClient())
                    {
                        client.BaseAddress = new Uri(ApimProxyEndpoint);

                        var createUserContract = new ApimUserCreateOrUpdateContact
                        {
                            FirstName = model.FirstName,
                            LastName = model.LastName,
                            Email = model.Email,
                            Password = model.Password,
                            State = "active"
                        };

                        var contentStr = SerializeToJson(createUserContract);

                        var request = new HttpRequestMessage(HttpMethod.Put, $"api/users/{user.Id}")
                        {
                            Content = new StringContent(contentStr, Encoding.Unicode, "application/json")
                        };

                        var response = await client.SendAsync(request);

                        if (response.IsSuccessStatusCode)
                        {
                            //User created successfully

                            await SignInAsync(user, isPersistent: false);

                            if (model.ReturnUrl != "")
                                return Redirect(model.ReturnUrl);
                            else
                                return RedirectToAction("Index", "Home");
                        }
                        else
                        {
                            @ViewBag.message = "APIM REST Connection Error: " + response.StatusCode;
                            return View();
                        }
                    }
                }
                else
                {
                    AddErrors(result);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        //
        // POST: /Account/Disassociate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Disassociate(string loginProvider, string providerKey)
        {
            ManageMessageId? message = null;
            IdentityResult result = await UserManager.RemoveLoginAsync(User.Identity.GetUserId(), new UserLoginInfo(loginProvider, providerKey));
            if (result.Succeeded)
            {
                message = ManageMessageId.RemoveLoginSuccess;
            }
            else
            {
                message = ManageMessageId.Error;
            }
            return RedirectToAction("Manage", new { Message = message });
        }

        //
        // GET: /Account/Manage
        public ActionResult Manage(ManageMessageId? message)
        {
            ViewBag.StatusMessage =
                message == ManageMessageId.ChangePasswordSuccess ? "Your password has been changed."
                : message == ManageMessageId.SetPasswordSuccess ? "Your password has been set."
                : message == ManageMessageId.RemoveLoginSuccess ? "The external login was removed."
                : message == ManageMessageId.Error ? "An error has occurred."
                : "";
            ViewBag.HasLocalPassword = HasPassword();
            ViewBag.ReturnUrl = Url.Action("Manage");
            return View();
        }

        //
        // POST: /Account/Manage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Manage(ManageUserViewModel model)
        {
            bool hasPassword = HasPassword();
            ViewBag.HasLocalPassword = hasPassword;
            ViewBag.ReturnUrl = Url.Action("Manage");
            if (hasPassword)
            {
                if (ModelState.IsValid)
                {
                    IdentityResult result = await UserManager.ChangePasswordAsync(User.Identity.GetUserId(), model.OldPassword, model.NewPassword);
                    if (result.Succeeded)
                    {
                        return RedirectToAction("Manage", new { Message = ManageMessageId.ChangePasswordSuccess });
                    }
                    else
                    {
                        AddErrors(result);
                    }
                }
            }
            else
            {
                // User does not have a password so remove any validation errors caused by a missing OldPassword field
                ModelState state = ModelState["OldPassword"];
                if (state != null)
                {
                    state.Errors.Clear();
                }

                if (ModelState.IsValid)
                {
                    IdentityResult result = await UserManager.AddPasswordAsync(User.Identity.GetUserId(), model.NewPassword);
                    if (result.Succeeded)
                    {
                        return RedirectToAction("Manage", new { Message = ManageMessageId.SetPasswordSuccess });
                    }
                    else
                    {
                        AddErrors(result);
                    }
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        //
        // POST: /Account/ExternalLogin
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult ExternalLogin(string provider, string returnUrl)
        {
            // Request a redirect to the external login provider
            return new ChallengeResult(provider, Url.Action("ExternalLoginCallback", "Account", new { ReturnUrl = returnUrl }));
        }

        //
        // GET: /Account/ExternalLoginCallback
        [AllowAnonymous]
        public async Task<ActionResult> ExternalLoginCallback(string returnUrl)
        {
            var loginInfo = await AuthenticationManager.GetExternalLoginInfoAsync();
            if (loginInfo == null)
            {
                return RedirectToAction("Login");
            }

            // Sign in the user with this external login provider if the user already has a login
            var user = await UserManager.FindAsync(loginInfo.Login);
            if (user != null)
            {
                await SignInAsync(user, isPersistent: false);
                return RedirectToLocal(returnUrl);
            }
            else
            {
                // If the user does not have an account, then prompt the user to create an account
                ViewBag.ReturnUrl = returnUrl;
                ViewBag.LoginProvider = loginInfo.Login.LoginProvider;
                return View("ExternalLoginConfirmation", new ExternalLoginConfirmationViewModel { UserName = loginInfo.DefaultUserName });
            }
        }

        //
        // POST: /Account/LinkLogin
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LinkLogin(string provider)
        {
            // Request a redirect to the external login provider to link a login for the current user
            return new ChallengeResult(provider, Url.Action("LinkLoginCallback", "Account"), User.Identity.GetUserId());
        }

        //
        // GET: /Account/LinkLoginCallback
        public async Task<ActionResult> LinkLoginCallback()
        {
            var loginInfo = await AuthenticationManager.GetExternalLoginInfoAsync(XsrfKey, User.Identity.GetUserId());
            if (loginInfo == null)
            {
                return RedirectToAction("Manage", new { Message = ManageMessageId.Error });
            }
            var result = await UserManager.AddLoginAsync(User.Identity.GetUserId(), loginInfo.Login);
            if (result.Succeeded)
            {
                return RedirectToAction("Manage");
            }
            return RedirectToAction("Manage", new { Message = ManageMessageId.Error });
        }

        //
        // POST: /Account/ExternalLoginConfirmation
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ExternalLoginConfirmation(ExternalLoginConfirmationViewModel model, string returnUrl)
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Manage");
            }

            if (ModelState.IsValid)
            {
                // Get the information about the user from the external login provider
                var info = await AuthenticationManager.GetExternalLoginInfoAsync();
                if (info == null)
                {
                    return View("ExternalLoginFailure");
                }
                var user = new ApplicationUser() { UserName = model.UserName };
                var result = await UserManager.CreateAsync(user);
                if (result.Succeeded)
                {
                    result = await UserManager.AddLoginAsync(user.Id, info.Login);
                    if (result.Succeeded)
                    {
                        await SignInAsync(user, isPersistent: false);
                        return RedirectToLocal(returnUrl);
                    }
                }
                AddErrors(result);
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        //
        // POST: /Account/LogOff
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LogOff()
        {
            AuthenticationManager.SignOut();
            return RedirectToAction("Index", "Home");
        }

        //
        // GET: /Account/ExternalLoginFailure
        [AllowAnonymous]
        public ActionResult ExternalLoginFailure()
        {
            return View();
        }

        [ChildActionOnly]
        public ActionResult RemoveAccountList()
        {
            var linkedAccounts = UserManager.GetLogins(User.Identity.GetUserId());
            ViewBag.ShowRemoveButton = HasPassword() || linkedAccounts.Count > 1;
            return (ActionResult)PartialView("_RemoveAccountPartial", linkedAccounts);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && UserManager != null)
            {
                UserManager.Dispose();
                UserManager = null;
            }
            base.Dispose(disposing);
        }

        public async Task<ActionResult> Delegate()
        {
            string returnUrl = Request.QueryString["returnUrl"];
            string productId = Request.QueryString["productId"];
            string subscriptionId = Request.QueryString["subscriptionId"];
            string userId = Request.QueryString["userId"];
            string salt = Request.QueryString["salt"];
            string signature;


            //First, validate the signature of the request

            var encoder = new HMACSHA512(Convert.FromBase64String(ApimDelegationKey));

            switch (Request.QueryString["operation"])
            {
                case "SignIn":
                    signature = Convert.ToBase64String(encoder.ComputeHash(Encoding.UTF8.GetBytes(salt + "\n" + returnUrl)));
                    break;
                case "SignOut":
                    signature = Convert.ToBase64String(encoder.ComputeHash(Encoding.UTF8.GetBytes(salt + "\n" + userId)));
                    break;
                case "Subscribe":
                    signature = Convert.ToBase64String(encoder.ComputeHash(Encoding.UTF8.GetBytes(salt + "\n" + productId + "\n" + userId)));
                    break;
                case "Unsubscribe":
                    signature = Convert.ToBase64String(encoder.ComputeHash(Encoding.UTF8.GetBytes(salt + "\n" + subscriptionId)));
                    break;
                case "ChangeProfile":
                case "ChangePassword":
                    signature = Convert.ToBase64String(encoder.ComputeHash(Encoding.UTF8.GetBytes(salt + "\n" + userId)));
                    break;
                case "CloseAccount":
                    signature = Convert.ToBase64String(encoder.ComputeHash(Encoding.UTF8.GetBytes(salt + "\n" + userId)));
                    break;
                default:
                    signature = "";
                    break;
            }

            if (signature == Request.QueryString["sig"])
            {
                //Signature matches / delegation request is legitimate
                //Now, process the request

                switch (Request.QueryString["operation"])
                {
                    case "SignIn":
                        if (!User.Identity.IsAuthenticated)
                            //User not authenticated, so ask them to go through login flow first
                            return RedirectToAction("LogIn", "Account");
                        else
                        {
                            //User is authenticated, so get SSO token and login
                            //create user in APIM as well
                            using (var client = new HttpClient())
                            {
                                client.BaseAddress = new Uri(ApimProxyEndpoint);
                                var request = new HttpRequestMessage(HttpMethod.Post, $"api/users/{User.Identity.GetUserId()}/generatessourl");

                                var response = await client.SendAsync(request);

                                if (response.IsSuccessStatusCode)
                                {
                                    var userSsoResult = await response.Content.ReadAsAsync<ApimSsoUrlResult>();
                                    return Redirect(userSsoResult.Value);
                                }
                                else
                                {
                                    @ViewBag.Message = "APIM REST Connection Error: " + response.StatusCode;
                                    return View();
                                }
                            }
                        }
                    case "SignOut":
                        AuthenticationManager.SignOut();
                        return Redirect(ApimDevPortalSignoutUrl);
                    case "Subscribe":
                    case "Unsubscribe":
                        return RedirectToAction("Product", "Account", new { operation = Request.QueryString["operation"], returnUrl = Request.QueryString["returnUrl"], productId = Request.QueryString["productId"], userId = Request.QueryString["userId"], subscriptionId = Request.QueryString["subscriptionId"] });
                    case "ChangeProfile":
                    case "ChangePassword":
                        return RedirectToAction("Manage", "Account", new { returnUrl = Request.QueryString["returnUrl"] });
                    case "CloseAccount":
                        // we are not handling this case, just signout
                        return Redirect(ApimDevPortalSignoutUrl);
                    default:
                    return View();
                }
            }
            else
            {
                ViewBag.Message = "Signature validation failed";
                return View();
            }
        }

        [AllowAnonymous]
        public async Task<ActionResult> Product()
        {
            ViewBag.ProductId = Request.QueryString["productId"];
            ViewBag.UserId = Request.QueryString["userId"];
            ViewBag.SubscriptionId = Request.QueryString["subscriptionId"];
            ViewBag.Operation = Request.QueryString["operation"];
            ViewBag.ReturnUrl = Request.QueryString["returnUrl"];

            //Set the proper title based on the operation at hadnd
            ViewBag.Title = "Product Subscription";

            switch (Request.QueryString["operation"])
            {
                case "Subscribe":
                    ViewBag.Message = "Would you like to subscribe for the product with ID " + ViewBag.ProductId + "?";
                    ViewBag.ButtonText = "Subscribe";
                    break;
                case "Unsubscribe":
                    ViewBag.Message = "Would you like to unsubscribe from the product with ID " + ViewBag.ProductId + "?";
                    ViewBag.ButtonText = "Unsubscribe";
                    break;
            }

            //If this is the confirmation of the page, perform needed operation
            if (Request.QueryString["action"] == "confirm")
            {
                //Register user for product
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri(ApimProxyEndpoint);

                    HttpResponseMessage response;

                    switch (Request.QueryString["operation"])
                    {
                        case "Subscribe":
                            var createSubscriptionContact = new ApimSubscriptionCreateOrUpdateContract
                            {
                                OwnerId = "users/" + Request.QueryString["userId"],
                                Scope = "products/" + Request.QueryString["productId"],
                                State = "active"
                            };

                            var contentStr = SerializeToJson(createSubscriptionContact);

                            var subscriptionId = Guid.NewGuid();
                            var request = new HttpRequestMessage(HttpMethod.Put, $"api/subscriptions/{subscriptionId}")
                            {
                                Content = new StringContent(contentStr, Encoding.Unicode, "application/json")
                            };

                            response = await client.SendAsync(request);
                            break;

                        case "Unsubscribe":
                            response = await client.DeleteAsync("api/subscriptions/" + Request.QueryString["subscriptionId"]);
                            break;

                        default:
                            response = new HttpResponseMessage(System.Net.HttpStatusCode.Unused);
                            break;
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        //Subscription created or cancelled

                        //return Redirect(Request.QueryString["returnUrl"]);
                        return Redirect(ApimDevPortalDevUrl);
                    }
                }
            }

            return View();
        }

        #region Helpers
        // Used for XSRF protection when adding external logins
        private const string XsrfKey = "XsrfId";

        private IAuthenticationManager AuthenticationManager
        {
            get
            {
                return HttpContext.GetOwinContext().Authentication;
            }
        }

        private async Task SignInAsync(ApplicationUser user, bool isPersistent)
        {
            AuthenticationManager.SignOut(DefaultAuthenticationTypes.ExternalCookie);
            var identity = await UserManager.CreateIdentityAsync(user, DefaultAuthenticationTypes.ApplicationCookie);
            AuthenticationManager.SignIn(new AuthenticationProperties() { IsPersistent = isPersistent }, identity);
        }

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error);
            }
        }

        private bool HasPassword()
        {
            var user = UserManager.FindById(User.Identity.GetUserId());
            if (user != null)
            {
                return user.PasswordHash != null;
            }
            return false;
        }

        public enum ManageMessageId
        {
            ChangePasswordSuccess,
            SetPasswordSuccess,
            RemoveLoginSuccess,
            Error
        }

        private ActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            else
            {
                return RedirectToAction("Index", "Home");
            }
        }

        private class ChallengeResult : HttpUnauthorizedResult
        {
            public ChallengeResult(string provider, string redirectUri)
                : this(provider, redirectUri, null)
            {
            }

            public ChallengeResult(string provider, string redirectUri, string userId)
            {
                LoginProvider = provider;
                RedirectUri = redirectUri;
                UserId = userId;
            }

            public string LoginProvider { get; set; }
            public string RedirectUri { get; set; }
            public string UserId { get; set; }

            public override void ExecuteResult(ControllerContext context)
            {
                var properties = new AuthenticationProperties() { RedirectUri = RedirectUri };
                if (UserId != null)
                {
                    properties.Dictionary[XsrfKey] = UserId;
                }
                context.HttpContext.GetOwinContext().Authentication.Challenge(properties, LoginProvider);
            }
        }
        #endregion
    }
}