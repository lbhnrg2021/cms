﻿using System;
using System.Collections.Generic;
using BaiRong.Core;
using BaiRong.Core.Data;
using BaiRong.Core.Model.Enumerations;
using Newtonsoft.Json;
using SiteServer.CMS.Core;
using SiteServer.CMS.Core.Permissions;
using SiteServer.Plugin;
using SiteServer.Plugin.Data;

namespace SiteServer.CMS.Plugin
{
    public class PublicApiInstance : IPublicApi
    {
        private readonly PluginMetadata _metadata;

        public PublicApiInstance(PluginMetadata metadata)
        {
            _metadata = metadata;
        }

        private string _databaseType;
        public string DatabaseType
        {
            get
            {
                if (_databaseType != null) return _databaseType;

                _databaseType = EDatabaseTypeUtils.GetValue(WebConfigUtils.DatabaseType);
                if (string.IsNullOrEmpty(_metadata.DatabaseType)) return _databaseType;

                _databaseType = _metadata.DatabaseType;
                if (WebConfigUtils.IsProtectData)
                {
                    _databaseType = TranslateUtils.DecryptStringBySecretKey(_databaseType);
                }
                return _databaseType;
            }
        }

        private string _connectionString;
        public string ConnectionString
        {
            get
            {
                if (_connectionString != null) return _connectionString;

                _connectionString = WebConfigUtils.ConnectionString;
                if (string.IsNullOrEmpty(_metadata.ConnectionString)) return _connectionString;

                _connectionString = _metadata.ConnectionString;
                if (WebConfigUtils.IsProtectData)
                {
                    _connectionString = TranslateUtils.DecryptStringBySecretKey(_connectionString);
                }
                return _connectionString;
            }
        }

        private IDbHelper _dbHelper;
        public IDbHelper DbHelper
        {
            get
            {
                if (_dbHelper != null) return _dbHelper;

                if (EDatabaseTypeUtils.Equals(DatabaseType, EDatabaseType.MySql))
                {
                    _dbHelper = new MySql();
                }
                else
                {
                    _dbHelper = new SqlServer();
                }
                return _dbHelper;
            }
        }

        public int GetSiteIdByFilePath(string path)
        {
            var publishmentSystemInfo = PathUtility.GetPublishmentSystemInfo(path);
            return publishmentSystemInfo?.PublishmentSystemId ?? 0;
        }

        public string GetSiteDirectoryPath(int siteId)
        {
            if (siteId <= 0) return null;

            var publishmentSystemInfo = PublishmentSystemManager.GetPublishmentSystemInfo(siteId);
            return publishmentSystemInfo == null ? null : PathUtility.GetPublishmentSystemPath(publishmentSystemInfo);
        }

        public void AddErrorLog(Exception ex)
        {
            LogUtils.AddErrorLog(ex, $"插件： {_metadata.Name}");
        }

        public List<int> GetSiteIds()
        {
            return PublishmentSystemManager.GetPublishmentSystemIdList();
        }

        public bool SetConfig(int siteId, string name, object config)
        {
            if (string.IsNullOrEmpty(name)) return false;

            try
            {
                if (config == null)
                {
                    DataProvider.PluginConfigDao.Delete(_metadata.Id, siteId, name);
                }
                else
                {
                    var settings = new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    };
                    var json = JsonConvert.SerializeObject(config, Formatting.Indented, settings);
                    if (DataProvider.PluginConfigDao.IsExists(_metadata.Id, siteId, name))
                    {
                        DataProvider.PluginConfigDao.Update(_metadata.Id, siteId, name, json);
                    }
                    else
                    {
                        DataProvider.PluginConfigDao.Insert(_metadata.Id, siteId, name, json);
                    }
                }
            }
            catch (Exception ex)
            {
                AddErrorLog(ex);
                return false;
            }
            return true;
        }

        public T GetConfig<T>(int siteId, string name)
        {
            if (string.IsNullOrEmpty(name)) return default(T);

            try
            {
                var value = DataProvider.PluginConfigDao.GetValue(_metadata.Id, siteId, name);
                if (!string.IsNullOrEmpty(value))
                {
                    return JsonConvert.DeserializeObject<T>(value);
                }
            }
            catch (Exception ex)
            {
                AddErrorLog(ex);
            }
            return default(T);
        }

        public bool RemoveConfig(int siteId, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            try
            {
                DataProvider.PluginConfigDao.Delete(_metadata.Id, siteId, name);
            }
            catch (Exception ex)
            {
                AddErrorLog(ex);
                return false;
            }
            return true;
        }

        public bool SetGlobalConfig(string name, object config)
        {
            return SetConfig(0, name, config);
        }

        public T GetGlobalConfig<T>(string name)
        {
            return GetConfig<T>(0, name);
        }

        public bool RemoveGlobalConfig(string name)
        {
            return RemoveConfig(0, name);
        }

        public void MoveFiles(int sourceSiteId, int targetSiteId, List<string> relatedUrls)
        {
            if (sourceSiteId == targetSiteId) return;

            var publishmentSystemInfo = PublishmentSystemManager.GetPublishmentSystemInfo(sourceSiteId);
            var targetPublishmentSystemInfo = PublishmentSystemManager.GetPublishmentSystemInfo(targetSiteId);
            if (publishmentSystemInfo == null || targetPublishmentSystemInfo == null) return;

            foreach (var relatedUrl in relatedUrls)
            {
                if (!string.IsNullOrEmpty(relatedUrl) && !PageUtils.IsProtocolUrl(relatedUrl))
                {
                    FileUtility.MoveFile(publishmentSystemInfo, targetPublishmentSystemInfo, relatedUrl);
                }
            }
        }

        public bool IsAuthorized()
        {
            var body = new RequestBody();
            return PermissionsManager.HasAdministratorPermissions(body.AdministratorName, _metadata.Id);
        }

        public bool IsSiteAuthorized(int siteId)
        {
            var body = new RequestBody();
            return PermissionsManager.HasAdministratorPermissions(body.AdministratorName, _metadata.Id + siteId);
        }

        public string GetUploadFilePath(int siteId, string fileName)
        {
            var publishmentSystemInfo = PublishmentSystemManager.GetPublishmentSystemInfo(siteId);
            var localDirectoryPath = PathUtility.GetUploadDirectoryPath(publishmentSystemInfo, PathUtils.GetExtension(fileName));
            var localFileName = PathUtility.GetUploadFileName(publishmentSystemInfo, fileName);
            return PathUtils.Combine(localDirectoryPath, localFileName);
        }

        public string GetUrlByFilePath(string filePath)
        {
            var siteId = GetSiteIdByFilePath(filePath);
            var publishmentSystemInfo = PublishmentSystemManager.GetPublishmentSystemInfo(siteId);
            return PageUtility.GetPublishmentSystemUrlByPhysicalPath(publishmentSystemInfo, filePath);
        }

        public string GetPluginUrl(int siteId, string relatedUrl = "")
        {
            var publishmentSystemInfo = PublishmentSystemManager.GetPublishmentSystemInfo(siteId);
            var apiUrl = PageUtility.GetOuterApiUrl(publishmentSystemInfo);
            return PageUtility.GetSiteFilesUrl(apiUrl, PageUtils.Combine(DirectoryUtils.SiteFiles.Plugins, _metadata.Id, relatedUrl));
        }

        public string GetPluginRestfulApiUrl(int siteId, string name = "", int id = 0)
        {
            var publishmentSystemInfo = PublishmentSystemManager.GetPublishmentSystemInfo(siteId);
            var apiUrl = PageUtility.GetOuterApiUrl(publishmentSystemInfo);
            return Controllers.Plugins.Restful.GetUrl(apiUrl, _metadata.Id, name, id);
        }

        public string GetPluginHttpApiUrl(int siteId, string name = "", int id = 0)
        {
            var publishmentSystemInfo = PublishmentSystemManager.GetPublishmentSystemInfo(siteId);
            var apiUrl = PageUtility.GetOuterApiUrl(publishmentSystemInfo);
            return Controllers.Plugins.Http.GetUrl(apiUrl, _metadata.Id, name, id);
        }

        public IPublishmentSystemInfo GetPublishmentSystemInfo(int siteId)
        {
            return PublishmentSystemManager.GetPublishmentSystemInfo(siteId);
        }

        public INodeInfo GetNodeInfo(int siteId, int channelId)
        {
            return NodeManager.GetNodeInfo(siteId, channelId);
        }

        public IContentInfo GetContentInfo(int siteId, int channelId, int contentId)
        {
            return DataProvider.ContentDao.GetContentInfo(siteId, channelId, contentId);
        }
    }
}
