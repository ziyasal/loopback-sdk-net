﻿using System;
using System.Collections.Generic;
using LoopBack.Sdk.Xamarin.Common;
using LoopBack.Sdk.Xamarin.Remooting.Adapters;
using Newtonsoft.Json.Linq;

namespace LoopBack.Sdk.Xamarin.Loopback
{
    /// <summary>
    ///  * A base class implementing <see cref="ModelRepository{T}"/> for the built-in User type.
    ///<pre>
    /// <code>UserRepository{MyUser} userRepo = new UserRepository{MyUser}("user", typeof(MyUser));
    /// </code>
    /// </pre>
    ///Most application are extending the built-in User model and adds new properties
    ///like address, etc. You should create your own Repository class
    ///by extending this base class in such case.
    ///<p>
    /// <code>
    /// public class Customer extends User {
    ///  // your custom properties and prototype (instance) methods
    ///}
    /// </code>
    ///</p>
    /// <pre>
    /// <code>
    /// public class CustomerRepository: UserRepository{Customer}
    /// {
    ///     public CustomerRepository():base("customer", null, typeof(Customer))
    ///     {
    ///     }
    ///     // your custom methods
    /// }
    ///</code>
    /// </pre>
    /// </summary>
    /// <typeparam name="T"> User implemenentation based on <see cref="User"/></typeparam>
    public class UserRepository<T> : ModelRepository<T> where T : User
    {
        public static string SHARED_PREFERENCES_NAME = "RestAdapter";
        public static string PROPERTY_CURRENT_USER_ID = "currentUserId";

        //TODO:
        private readonly ISharedReferencesService _sharedReferencesService;

        private AccessTokenRepository _accessTokenRepository;

        private bool isCurrentUserIdLoaded;
        private T _cachedCurrentUser;
        private object _currentUserId;


        //TODO : typical usage
        /// <summary>
        /// Get the cached value of the currently logged in user.
        /// </summary>
        public T CachedCurrentUser
        {
            get { return _cachedCurrentUser; }
        }

        /// <summary>
        /// Id of the currently logged in user. null when there is no user logged in.
        /// </summary>
        public object CurrentUserId
        {
            get
            {
                LoadCurrentUserIdIfNotLoaded();
                return _currentUserId;
            }
            set
            {
                _currentUserId = value;
                _cachedCurrentUser = null;
                SaveCurrentUserId();
            }
        }

        /// <summary>
        /// Creates a new UserRepository, associating it with the static {T} user class and the user class name.
        /// </summary>
        /// <param name="className">The remote class name.</param>
        /// <param name="userClass">The User (sub)class. It must have a public no-argument constructor.</param>
        public UserRepository(string className, Type userClass)
            : this(className, null, userClass)
        {

        }

        /// <summary>
        /// Creates a new UserRepository, associating it with the static {T} user class and the user class name.
        /// </summary>
        /// <param name="className">The remote class name.</param>
        /// <param name="nameForRestUrl">The pluralized class name to use in REST transport. Use <code>null</code> for the default value, which is the plural form of className.</param>
        /// <param name="userClass">The User (sub)class. It must have a public no-argument constructor.</param>
        public UserRepository(string className, string nameForRestUrl, Type userClass)
            : base(className, nameForRestUrl, userClass)
        {

        }

        public UserRepository()
            : base("user", typeof(User))
        {
        }


        /// <summary>
        /// Creates a {T} user instance given an email and a password.
        /// </summary>
        /// <param name="email">email</param>
        /// <param name="password">password</param>
        /// <returns>A {T} user instance.</returns>
        public T CreateUser(string email, string password)
        {
            Dictionary<string, object> dictionary = new Dictionary<string, object>();
            dictionary.Add("email", email);
            dictionary.Add("password", password);

            T user = CreateObject(dictionary);
            return user;
        }

        /// <summary>
        /// Creates a <see cref="RestContract"/> representing the user type's custom
        ///routes. Used to extend an <see cref="Adapter"/> to support user. Calls
        /// super <see cref="ModelRepository{T}"/> createContract first.
        /// </summary>
        /// <returns>A <see cref="RestContract"/> for this model type.</returns>

        public override RestContract CreateContract()
        {
            RestContract contract = base.CreateContract();

            contract.AddItem(new RestContractItem("/" + NameForRestUrl + "/login?include=user", "POST"), ClassName + ".login");
            contract.AddItem(new RestContractItem("/" + NameForRestUrl + "/logout", "POST"), ClassName + ".logout");
            return contract;
        }

        /// <summary>
        /// Creates a new {T} user given the email, password and optional parameters.
        /// </summary>
        /// <param name="email">user email</param>
        /// <param name="password">user password</param>
        /// <param name="parameters">optional parameters</param>
        /// <returns>A new {T} user instance.</returns>
        public T CreateUser(String email, String password, Dictionary<string, object> parameters)
        {
            Dictionary<string, object> allParams = new Dictionary<string, object>();
            allParams.AddRange(parameters);
            allParams.Add("email", email);
            allParams.Add("password", password);
            T user = CreateObject(allParams);

            return user;
        }


        /// <summary>
        /// Login a user given an email and password. Creates a <see cref="AccessToken"/> and {T} user models if successful.
        /// </summary>
        /// <param name="email">user email</param>
        /// <param name="password">user password</param>
        /// <param name="onSuccess">The callback to invoke when the execution finished with success</param>
        /// <param name="onError">The callback to invoke when the execution finished with error</param>
        public void LoginUSer(string email, string password, Action<AccessToken, T> onSuccess, Action<Exception> onError)
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            parameters.Add("email", email);
            parameters.Add("password", password);

            InvokeStaticMethod("login", parameters, response =>
            {
                try
                {
                    Dictionary<string, object> creationParameters = response.ToDictionaryFromJson();

                    AccessToken token = GetAccessTokenRepository().CreateObject(creationParameters);
                    GetRestAdapter().AccessToken = token.Id.ToString();

                    JToken userJson = JObject.Parse(response)["user"];
                    Dictionary<string, object> dictionaryFromJson = userJson.ToString().ToDictionaryFromJson();

                    T user = userJson != null
                            ? CreateObject(dictionaryFromJson)
                            : null;

                    CurrentUserId = token.UserId;
                    _cachedCurrentUser = user;

                    onSuccess(token, user);
                }
                catch (Exception)
                {
                    //TODO:log
                }

            }, onError);
        }

        /// <summary>
        /// Logs the current user out of the server and removes the access token from the system.
        /// </summary>
        /// <param name="onSuccess">The callback to invoke when the execution finished with success</param>
        /// <param name="onError">The callback to invoke when the execution finished with error</param>
        public void Logout(Action onSuccess, Action<Exception> onError)
        {
            InvokeStaticMethod("logout", null, response =>
            {
                try
                {
                    RestAdapter radapter = GetRestAdapter();
                    radapter.ClearAccessToken();
                    CurrentUserId = null;
                    onSuccess();
                }
                catch (Exception)
                {
                    //TODO:Log
                }

            }, onError);
        }


        private AccessTokenRepository GetAccessTokenRepository()
        {
            return _accessTokenRepository ?? (_accessTokenRepository = GetRestAdapter().CreateRepository<AccessTokenRepository, AccessToken>());
        }

        private void SaveCurrentUserId()
        {
            //Save to shared cache
        }

        private void LoadCurrentUserIdIfNotLoaded()
        {
            //Load it from shared cache
        }

        //TODO:SharedPreferences inteface
    }
}