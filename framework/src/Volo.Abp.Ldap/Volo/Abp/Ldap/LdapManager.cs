﻿using System;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;
using Volo.Abp.ExceptionHandling;
using Volo.Abp.Ldap.Exceptions;
using Volo.Abp.Ldap.Modeling;

namespace Volo.Abp.Ldap
{
    public class LdapManager : ILdapManager, ITransientDependency
    {
        protected AbpLdapOptions LdapOptions { get; }
        protected IHybridServiceScopeFactory HybridServiceScopeFactory { get; }

        private readonly string[] _attributes =
        {
            "objectCategory", "objectClass", "cn", "name", "distinguishedName",
            "ou",
            "sAMAccountName", "userPrincipalName", "telephoneNumber", "mail"
        };

        public LdapManager(IOptions<AbpLdapOptions> ldapSettingsOptions, IHybridServiceScopeFactory hybridServiceScopeFactory)
        {
            HybridServiceScopeFactory = hybridServiceScopeFactory;
            LdapOptions = ldapSettingsOptions.Value;
        }

        #region Organization
        /// <summary>
        /// query the specified organizations.
        ///
        /// filter: (&(name=xxx)(objectClass=organizationalUnit)) when name is not null
        /// filter: (&(objectClass=organizationalUnit)) when name is null
        ///
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public virtual IList<LdapOrganization> GetOrganizations(string name = null)
        {
            var conditions = new Dictionary<string, string>
            {
                {"name", name},
                {"objectClass", "organizationalUnit"},
            };
            return Query<LdapOrganization>(LdapOptions.SearchBase, conditions);
        }

        /// <summary>
        /// query the specified organization.
        ///
        /// filter: (&(distinguishedName=xxx)(objectClass=organizationalUnit)) when organizationName is not null
        ///
        /// </summary>
        /// <param name="distinguishedName"></param>
        /// <returns></returns>
        public virtual LdapOrganization GetOrganization(string distinguishedName)
        {
            distinguishedName = Check.NotNullOrWhiteSpace(distinguishedName, nameof(distinguishedName));
            var conditions = new Dictionary<string, string>
            {
                {"distinguishedName", distinguishedName},
                {"objectClass", "organizationalUnit"},
            };
            return QueryOne<LdapOrganization>(LdapOptions.SearchBase, conditions);
        }

        public virtual void AddSubOrganization(string organizationName, LdapOrganization parentOrganization)
        {
            organizationName = Check.NotNullOrWhiteSpace(organizationName, nameof(organizationName));
            var dn = $"OU={organizationName},{parentOrganization.DistinguishedName}";

            var attributeSet = new LdapAttributeSet
            {
                new LdapAttribute("objectCategory", $"CN=Organizational-Unit,CN=Schema,CN=Configuration,{LdapOptions.DomainDistinguishedName}"),
                new LdapAttribute("objectClass", new[] {"top", "organizationalUnit"}),
                new LdapAttribute("name", organizationName),
            };

            var newEntry = new LdapEntry(dn, attributeSet);

            using (var ldapConnection = GetConnection())
            {
                ldapConnection.Add(newEntry);
            }
        }

        public virtual void AddSubOrganization(string organizationName, string parentDistinguishedName)
        {
            organizationName = Check.NotNullOrWhiteSpace(organizationName, nameof(organizationName));
            parentDistinguishedName =
                Check.NotNullOrWhiteSpace(parentDistinguishedName, nameof(parentDistinguishedName));

            var parentOrganization = GetOrganization(parentDistinguishedName);
            if (null == parentOrganization)
            {
                throw new OrganizationNotExistException(parentDistinguishedName);
            }

            AddSubOrganization(organizationName, parentOrganization);
        }

        #endregion

        #region User
        /// <summary>
        /// query the specified users.
        ///
        /// filter: (&(name=xxx)(objectCategory=person)(objectClass=user)) when name is not null
        /// filter: (&(objectCategory=person)(objectClass=user)) when name is null
        ///
        /// filter: (&(displayName=xxx)(objectCategory=person)(objectClass=user)) when displayName is not null
        /// filter: (&(objectCategory=person)(objectClass=user)) when displayName is null
        ///
        /// filter: (&(cn=xxx)(objectCategory=person)(objectClass=user)) when commonName is not null
        /// filter: (&(objectCategory=person)(objectClass=user)) when commonName is null
        ///
        /// </summary>
        /// <param name="name"></param>
        /// <param name="displayName"></param>
        /// <param name="commonName"></param>
        /// <returns></returns>
        public virtual IList<LdapUser> GetUsers(string name = null, string displayName = null, string commonName = null)
        {
            var conditions = new Dictionary<string, string>
            {
                {"objectCategory", "person"},
                {"objectClass", "user"},
                {"name", name},
                {"displayName", displayName},
                {"cn", commonName},
            };
            return Query<LdapUser>(LdapOptions.SearchBase, conditions);
        }

        /// <summary>
        /// query the specified User.
        ///
        /// filter: (&(distinguishedName=xxx)(objectCategory=person)(objectClass=user)) when distinguishedName is not null
        ///
        /// </summary>
        /// <param name="distinguishedName"></param>
        /// <returns></returns>
        public virtual LdapUser GetUser(string distinguishedName)
        {
            distinguishedName = Check.NotNullOrWhiteSpace(distinguishedName, nameof(distinguishedName));
            var conditions = new Dictionary<string, string>
            {
                {"objectCategory", "person"},
                {"objectClass", "user"},
                {"distinguishedName", distinguishedName},
            };
            return QueryOne<LdapUser>(LdapOptions.SearchBase, conditions);
        }

        public virtual void AddUserToOrganization(string userName, string password, LdapOrganization parentOrganization)
        {
            var dn = $"CN={userName},{parentOrganization.DistinguishedName}";
            var mail = $"{userName}@{LdapOptions.DomainName}";
            var encodedBytes = SupportClass.ToSByteArray(Encoding.Unicode.GetBytes($"\"{password}\""));

            var attributeSet = new LdapAttributeSet
            {
                new LdapAttribute("instanceType", "4"),
                new LdapAttribute("objectCategory", $"CN=Person,CN=Schema,CN=Configuration,{LdapOptions.DomainDistinguishedName}"),
                new LdapAttribute("objectClass", new[] {"top", "person", "organizationalPerson", "user"}),
                new LdapAttribute("name", userName),
                new LdapAttribute("cn", userName),
                new LdapAttribute("sAMAccountName", userName),
                new LdapAttribute("userPrincipalName", userName),
                new LdapAttribute("sn", userName),
                new LdapAttribute("displayName", userName),
                new LdapAttribute("unicodePwd", encodedBytes),
                new LdapAttribute("userAccountControl",  "512"),
                new LdapAttribute("mail", mail),
            };
            var newEntry = new LdapEntry(dn, attributeSet);

            using (var ldapConnection = GetConnection())
            {
                ldapConnection.Add(newEntry);
            }
        }

        public virtual void AddUserToOrganization(string userName, string password, string parentDistinguishedName)
        {
            var dn = $"CN={userName},{parentDistinguishedName}";
            var mail = $"{userName}@{LdapOptions.DomainName}";
            sbyte[] encodedBytes = SupportClass.ToSByteArray(Encoding.Unicode.GetBytes($"\"{password}\""));

            var attributeSet = new LdapAttributeSet
            {
                new LdapAttribute("instanceType", "4"),
                new LdapAttribute("objectCategory", $"CN=Person,CN=Schema,CN=Configuration,{LdapOptions.DomainDistinguishedName}"),
                new LdapAttribute("objectClass", new[] {"top", "person", "organizationalPerson", "user"}),
                new LdapAttribute("name", userName),
                new LdapAttribute("cn", userName),
                new LdapAttribute("sAMAccountName", userName),
                new LdapAttribute("userPrincipalName", userName),
                new LdapAttribute("sn", userName),
                new LdapAttribute("displayName", userName),
                new LdapAttribute("unicodePwd", encodedBytes),
                new LdapAttribute("userAccountControl",  "512"),
                new LdapAttribute("mail", mail),
            };
            var newEntry = new LdapEntry(dn, attributeSet);

            using (var ldapConnection = GetConnection())
            {
                ldapConnection.Add(newEntry);
            }
        }

        #endregion

        #region Authenticate

        /// <summary>
        /// Authenticate
        /// </summary>
        /// <param name="userDomainName">E.g administrator@yourdomain.com.cn </param>
        /// <param name="password"></param>
        /// <returns></returns>
        public virtual bool Authenticate(string userDomainName, string password)
        {
            try
            {
                using (GetConnection(userDomainName, password))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                using (var scope = HybridServiceScopeFactory.CreateScope())
                {
                    scope.ServiceProvider
                        .GetRequiredService<IExceptionNotifier>()
                        .NotifyAsync(ex);
                }

                return false;
            }
        }

        #endregion

        protected virtual ILdapConnection GetConnection(string bindUserName = null, string bindUserPassword = null)
        {
            // bindUserName/bindUserPassword only be used when authenticate
            bindUserName = bindUserName ?? LdapOptions.Credentials.DomainUserName;
            bindUserPassword = bindUserPassword ?? LdapOptions.Credentials.Password;

            var ldapConnection = new LdapConnection() { SecureSocketLayer = LdapOptions.UseSsl };
            if (LdapOptions.UseSsl)
            {
                ldapConnection.UserDefinedServerCertValidationDelegate += (sender, certificate, chain, sslPolicyErrors) => true;
            }
            ldapConnection.Connect(LdapOptions.ServerHost, LdapOptions.ServerPort);

            if (LdapOptions.UseSsl)
            {
                ldapConnection.Bind(LdapConnection.Ldap_V3, bindUserName, bindUserPassword);
            }
            else
            {
                ldapConnection.Bind(bindUserName, bindUserPassword);
            }
            return ldapConnection;
        }

        protected virtual IList<T> Query<T>(string searchBase, Dictionary<string, string> conditions) where T : class, ILdapEntry
        {
            var filter = LdapHelps.BuildFilter(conditions);

            var result = new List<T>();

            using (var ldapConnection = GetConnection())
            {
                var search = ldapConnection.Search(searchBase, LdapConnection.SCOPE_SUB, filter,
                    _attributes, false, null, null);

                LdapMessage message;
                while ((message = search.getResponse()) != null)
                {
                    if (!(message is LdapSearchResult searchResultMessage))
                    {
                        continue;
                    }
                    var entry = searchResultMessage.Entry;
                    if (typeof(T) == typeof(LdapOrganization))
                    {
                        result.Add(new LdapOrganization(entry.getAttributeSet()) as T);
                    }

                    if (typeof(T) == typeof(LdapUser))
                    {
                        result.Add(new LdapUser(entry.getAttributeSet()) as T);
                    }
                }
            }
            return result;
        }

        protected virtual T QueryOne<T>(string searchBase, Dictionary<string, string> conditions) where T : class, ILdapEntry
        {
            var filter = LdapHelps.BuildFilter(conditions);

            using (var ldapConnection = GetConnection())
            {
                var search = ldapConnection.Search(searchBase, LdapConnection.SCOPE_SUB, filter,
                    _attributes, false, null, null);

                LdapMessage message;
                while ((message = search.getResponse()) != null)
                {
                    if (!(message is LdapSearchResult searchResultMessage))
                    {
                        continue;
                    }
                    var entry = searchResultMessage.Entry;
                    if (typeof(T) == typeof(LdapOrganization))
                    {
                        return new LdapOrganization(entry.getAttributeSet()) as T;
                    }

                    if (typeof(T) == typeof(LdapUser))
                    {
                        return new LdapUser(entry.getAttributeSet()) as T;
                    }
                    return null;
                }
            }
            return null;
        }

    }
}
