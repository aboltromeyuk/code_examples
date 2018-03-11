using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Topshelf.Logging;
using System.Data.SqlTypes;

namespace DMTool.WindowsService.Services
{
    public class DigitalPlatformService : WebApiClientServiceBase, IDigitalPlatformService
    {
        private readonly LogWriter _logger;

        private readonly IRepository<Dealer> _dealersRepository;
        private readonly IRepository<NormHoursCost> _normHoursCostRepository;
        private readonly IRepository<CwOrderInfo> _cwOrderInfoRepository;
        private readonly IRepository<ScOrderInfo> _scOrderInfoRepository;
        private readonly IRepository<SpOrderInfo> _spOrderInfoRepository;
        private readonly IRepository<SoInspectionOrderInfo> _soOrderInfoRepository;
        private readonly IRepository<PpDealerPartLog> _ppDealerPartLogRepository;
        private readonly IRepository<PpDealerSpecialPartLog> _ppDealerSpecialPartLogRepository;

        private readonly string _baseUrl;
        private readonly string _tokenUrl;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _username;
        private readonly string _password;

        private string _token;
        private DateTime _tokenExpires;

        public DigitalPlatformService(
            LogWriter logger,

            IRepository<Dealer> dealersRepository,
            IRepository<NormHoursCost> normHoursCostRepository,
            IRepository<CwOrderInfo> cwOrderInfoRepository,
            IRepository<ScOrderInfo> scOrderInfoRepository,
            IRepository<SpOrderInfo> spOrderInfoRepository,
            IRepository<SoInspectionOrderInfo> soOrderInfoRepository,
            IRepository<PpDealerPartLog> ppDealerPartLogRepository,
            IRepository<PpDealerSpecialPartLog> ppDealerSpecialPartLogRepository,

            string baseUrl,
            string tokenUrl,
            string clientId,
            string clientSecret,
            string username,
            string password
        )
        {
            _logger = logger;

            _dealersRepository = dealersRepository;
            _normHoursCostRepository = normHoursCostRepository;
            _cwOrderInfoRepository = cwOrderInfoRepository;
            _scOrderInfoRepository = scOrderInfoRepository;
            _spOrderInfoRepository = spOrderInfoRepository;
            _soOrderInfoRepository = soOrderInfoRepository;
            _ppDealerPartLogRepository = ppDealerPartLogRepository;
            _ppDealerSpecialPartLogRepository = ppDealerSpecialPartLogRepository;

            _baseUrl = (baseUrl ?? "").TrimEnd('/') + "/";
            _tokenUrl = tokenUrl;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _username = username;
            _password = password;
        }

        public void UpdateDealers()
        {
            _logger.Debug("(Digital Platform) Dealers :: Update started");

            var dealers = _dealersRepository.Find(x => !string.IsNullOrEmpty(x.Gssn) && x.Gssn != "-").ToList();
            
            var successCount = 0;

            foreach (var dealer in dealers) {
                try {
                    var optionCompleteWheels = Send<DealerDto, CwDealerOptionsDto>(
                        HttpMethod.Get,
                        "api/CwDealerOptions",
                        new DealerDto { Gssn = dealer.Gssn }
                    );
                    var optionServicePrice = Send<DealerDto, SpDealerOptionsDto>(
                        HttpMethod.Get,
                        "api/SpDealerOptions",
                        new DealerDto { Gssn = dealer.Gssn }
                    );
                    var optionServiceContract = Send<DealerDto, ScDealerOptionsDto>(
                        HttpMethod.Get,
                        "api/ScDealerOptions",
                        new DealerDto { Gssn = dealer.Gssn }
                    );
                    var optionServiceOnline = Send<DealerDto, SoDealerOptionsDto>(
                        HttpMethod.Get,
                        "api/SoDealerOptions",
                        new DealerDto { Gssn = dealer.Gssn }
                    );
                    var normHoursCostUpdate = _normHoursCostRepository.Single(n => n.Dealer.Gssn == dealer.Gssn);

                    normHoursCostUpdate.CommHoursCost = optionServicePrice.WorkingHourPrice;
                    normHoursCostUpdate.UpdateTimeComm = optionServicePrice.DateUpdate == SqlDateTime.MinValue.Value ? null : optionServicePrice.DateUpdate;

                    dealer.ConnProject.CompleteWheels = optionCompleteWheels.Show;
                    dealer.ConnProject.ServicePrice = optionServicePrice.Show;
                    dealer.ConnProject.ServiceContract = optionServiceContract.Show;
                    dealer.ConnProject.ServiceOnline = optionServiceOnline.Show;

                    ++successCount;
                }

                catch (Exception e) {
                    _logger.ErrorFormat("(Digital Platform) Dealers :: \"{0}\" (CoFiCo {1}, GSSN {2})\r\n{3}", dealer.Name, dealer.CoFiCo, dealer.Gssn, e);
                }
            }

            if (successCount > 0) {
                _dealersRepository.SaveChanges();
                _normHoursCostRepository.SaveChanges();
            }

            _logger.InfoFormat("(Digital Platform) Dealers :: {0} of {1} dealers updated", successCount, dealers.Count);
        }        

        public void ImportCwOrdersInfo()
        {
            _logger.Debug("(Digital Platform) CwOrdersInfo :: Import started");

            var dealers = _dealersRepository.Find(x => !string.IsNullOrEmpty(x.Gssn) && x.Gssn != "-").ToList();                          
            
            var successCount = 0;
            var delFlag = 0;

            foreach (var dealer in dealers)
            {
                try
                {
                    var cwOrderInfo = Send<DealerDto, IEnumerable<CwOrderInfoDto>>(
                        HttpMethod.Get,
                        "api/CwOrder",
                        new DealerDto { Gssn = dealer.Gssn }
                    );

                    if (cwOrderInfo.Count() != 0 && delFlag==0)
                    {
                        _cwOrderInfoRepository.BatchDelete(_cwOrderInfoRepository.GetAll().AsQueryable());
                        delFlag += 1;
                    }
                                            

                    foreach(var item in cwOrderInfo)
                    {
                        _cwOrderInfoRepository.Add(new CwOrderInfo
                        {
                            DealerId = _dealersRepository.Single(d=>d.Gssn==dealer.Gssn).Id,
                            DateCreate = item.DateCreate,
                            DateProcessing = item.DateProcessing,
                            OrderStatus = item.OrderStatus
                        });
                    }                    

                    ++successCount;
                }

                catch (Exception e)
                {
                    _logger.ErrorFormat("(Digital Platform) CwOrdersInfo :: error impot for dealer: \"{0}\" (CoFiCo {1}, GSSN {2})\r\n{3}", dealer.Name, dealer.CoFiCo, dealer.Gssn, e);
                }
            }

            if (successCount > 0)
            {
                _cwOrderInfoRepository.SaveChanges();
            }

            _logger.InfoFormat("(Digital Platform) CwOrdersInfo :: imported for {0} dealers", successCount);
        }

        public void ImportScOrdersInfo()
        {
            _logger.Debug("(Digital Platform) ScOrdersInfo :: Import started");

            var dealers = _dealersRepository.Find(x => !string.IsNullOrEmpty(x.Gssn) && x.Gssn != "-").ToList();

            var successCount = 0;
            var delFlag = 0;

            foreach (var dealer in dealers)
            {
                try
                {
                    var scOrderInfo = Send<DealerDto, IEnumerable<ScOrderInfo>>(
                        HttpMethod.Get,
                        "api/ScOrder",
                        new DealerDto { Gssn = dealer.Gssn }
                    );

                    if (scOrderInfo.Count() != 0 && delFlag == 0)
                    {
                        _scOrderInfoRepository.BatchDelete(_scOrderInfoRepository.GetAll().AsQueryable());
                        delFlag++;
                    }


                    foreach (var item in scOrderInfo)
                    {
                        _scOrderInfoRepository.Add(new ScOrderInfo
                        {
                            DealerId = _dealersRepository.Single(d => d.Gssn == dealer.Gssn).Id,
                            DateCreate = item.DateCreate,
                            DateProcessing = item.DateProcessing,
                            OrderStatus = item.OrderStatus
                        });
                    }

                    ++successCount;
                }                

                catch (Exception e)
                {
                    _logger.ErrorFormat("(Digital Platform) ScOrdersInfo :: error impot for dealer: \"{0}\" (CoFiCo {1}, GSSN {2})\r\n{3}", dealer.Name, dealer.CoFiCo, dealer.Gssn, e);
                }
            }

            if (successCount > 0)
            {
                _scOrderInfoRepository.SaveChanges();
            }

            _logger.InfoFormat("(Digital Platform) ScOrdersInfo :: imported for {0} dealers", successCount);
        }

        public void ImportSoOrdersInfo()
        {
            _logger.Debug("(Digital Platform) SoOrdersInfo :: Import started");

            var dealers = _dealersRepository.Find(x => !string.IsNullOrEmpty(x.Gssn) && x.Gssn != "-").ToList();

            var successCount = 0;
            var delFlag = 0;

            foreach (var dealer in dealers)
            {
                try
                {
                    var soOrderInfo = Send<DealerDto, IEnumerable<SoInspectionOrderInfo>>(
                        HttpMethod.Get,
                        "api/SoOrder",
                        new DealerDto { Gssn = dealer.Gssn }
                    );

                    if (soOrderInfo.Count() != 0 && delFlag == 0)
                    {
                        _soOrderInfoRepository.BatchDelete(_soOrderInfoRepository.GetAll().AsQueryable());
                        delFlag++;
                    }


                    foreach (var item in soOrderInfo)
                    {
                        _soOrderInfoRepository.Add(new SoInspectionOrderInfo
                        {
                            DealerId = _dealersRepository.Single(d => d.Gssn == dealer.Gssn).Id,
                            DateCreate = item.DateCreate,
                            DateProcessing = item.DateProcessing,
                            OrderStatus = item.OrderStatus
                        });
                    }

                    ++successCount;
                }

                catch (Exception e)
                {
                    _logger.ErrorFormat("(Digital Platform) SoOrdersInfo :: error impot for dealer: \"{0}\" (CoFiCo {1}, GSSN {2})\r\n{3}", dealer.Name, dealer.CoFiCo, dealer.Gssn, e);
                }
            }

            if (successCount > 0)
            {
                _soOrderInfoRepository.SaveChanges();
            }

            _logger.InfoFormat("(Digital Platform) SoOrdersInfo :: imported for {0} dealers", successCount);
        }

        public void ImportSpOrdersInfo()
        {
            _logger.Debug("(Digital Platform) SpOrdersInfo :: Import started");

            var dealers = _dealersRepository.Find(x => !string.IsNullOrEmpty(x.Gssn) && x.Gssn != "-").ToList();

            var successCount = 0;
            var delFlag = 0;

            foreach (var dealer in dealers)
            {
                try
                {
                    var spOrderInfo = Send<DealerDto, IEnumerable<SpOrderInfo>>(
                        HttpMethod.Get,
                        "api/SpOrder",
                        new DealerDto { Gssn = dealer.Gssn }
                    );

                    if (spOrderInfo.Count() != 0 && delFlag == 0)
                    {
                        _spOrderInfoRepository.BatchDelete(_spOrderInfoRepository.GetAll().AsQueryable());
                        delFlag++;
                    }


                    foreach (var item in spOrderInfo)
                    {
                        _spOrderInfoRepository.Add(new SpOrderInfo
                        {
                            DealerId = _dealersRepository.Single(d => d.Gssn == dealer.Gssn).Id,
                            DateCreate = item.DateCreate,
                            DateProcessing = item.DateProcessing,
                            OrderStatus = item.OrderStatus
                        });
                    }

                    ++successCount;
                }

                catch (Exception e)
                {
                    _logger.ErrorFormat("(Digital Platform) SpOrdersInfo :: error impot for dealer: \"{0}\" (CoFiCo {1}, GSSN {2})\r\n{3}", dealer.Name, dealer.CoFiCo, dealer.Gssn, e);
                }
            }

            if (successCount > 0)
            {
                _soOrderInfoRepository.SaveChanges();
            }

            _logger.InfoFormat("(Digital Platform) SpOrdersInfo :: imported for {0} dealers", successCount);
        }

        public void UpdatePpDealerPartLog()
        {
            _logger.Debug("(Digital Platform) PpDealerPartLog :: Update started");

            var dealers = _dealersRepository.Find(x => !string.IsNullOrEmpty(x.Gssn) && x.Gssn != "-").ToList();
            var ppDealerPartLogsList = _ppDealerPartLogRepository.GetAll();
            var lastDateImport = ppDealerPartLogsList.Any() ? 
                                        ppDealerPartLogsList.OrderByDescending(l => l.DateImport).Select(l => l.DateImport).First() : DateTime.MinValue;

            try
            {
                var ppDealerPartLogsDtoForAdd = Send<PpDealerPartLogRequestDto, IEnumerable<PpDealerPartLogDto>>(
                        HttpMethod.Get,
                        "api/PpDealerPartLog",
                        new PpDealerPartLogRequestDto { LastTimeImport = lastDateImport.Value }
                    );

                foreach (var item in ppDealerPartLogsDtoForAdd)
                {
                    _ppDealerPartLogRepository.Add(new PpDealerPartLog
                    {
                        DealerId = dealers.Single(d=>d.Gssn == item.Gssn).Id,
                        DateImport = item.DateImport,
                        SuccessPartsCount = item.SuccessPartsCount
                    });
                }

                _ppDealerPartLogRepository.SaveChanges();

                _logger.InfoFormat("(Digital Platform) PpDealerPartLog :: {0} items inserted", ppDealerPartLogsDtoForAdd.Count());
            }
            catch(Exception e)
            {
                _logger.ErrorFormat("(Digital Platform) PpDealerPartLog :: Error: {0}", e);
            }            
        }

        public void UpdatePpDealerSpecialPartLog()
        {
            _logger.Debug("(Digital Platform) PpDealerSpecialPartLog :: Update started");

            var dealers = _dealersRepository.Find(x => !string.IsNullOrEmpty(x.Gssn) && x.Gssn != "-").ToList();
            var ppDealerSpecialPartLogsList = _ppDealerSpecialPartLogRepository.GetAll();

            var lastDateImport = ppDealerSpecialPartLogsList.Any() ?
                                        ppDealerSpecialPartLogsList.OrderByDescending(l => l.DateImport).Select(l => l.DateImport).First() : DateTime.MinValue;

            try
            {
                var ppDealerSpecialPartLogsDtoForAdd = Send<PpDealerPartLogRequestDto, IEnumerable<PpDealerSpecialPartLogDto>>(
                        HttpMethod.Get,
                        "api/PpDealerSpecialPartLog",
                        new PpDealerPartLogRequestDto { LastTimeImport = lastDateImport.Value }
                    );

                foreach (var item in ppDealerSpecialPartLogsDtoForAdd)
                {
                    _ppDealerSpecialPartLogRepository.Add(new PpDealerSpecialPartLog
                    {
                        DealerId = dealers.Single(d => d.Gssn == item.Gssn).Id,
                        DateImport = item.DateImport,
                        SuccessPartsCount = item.SuccessPartsCount
                    });
                }

                _ppDealerSpecialPartLogRepository.SaveChanges();

                _logger.InfoFormat("(Digital Platform) PpDealerSpecialPartLog :: {0} items inserted", ppDealerSpecialPartLogsDtoForAdd.Count());
            }
            catch (Exception e)
            {
                _logger.ErrorFormat("(Digital Platform) PpDealerSpecialPartLog :: Error: {0}", e);
            }
        }

        #region WebApiClientServiceBase

        protected override HttpClient CreateHttpClient()
        {
            return new HttpClient {
                BaseAddress = new Uri(_baseUrl)
            };
        }

        protected override HttpRequestMessage PrepareHttpRequest(HttpRequestMessage request)
        {
            if (string.IsNullOrEmpty(_token) || DateTime.Now > _tokenExpires) {
                var tokenRequest = new HttpRequestMessage(HttpMethod.Post, _tokenUrl) {
                    Headers = {
                        Authorization = new AuthenticationHeaderValue("Basic",
                            Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(
                                _clientId + ":" + _clientSecret))
                        )
                    },
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                        { "grant_type", "password" },
                        { "username", _username },
                        { "password", _password },
                        { "scope", "resourceProfile" }
                    })
                };

                var tokenResponse = GetAsyncResult(HttpClient.SendAsync(tokenRequest));

                if (!tokenResponse.IsSuccessStatusCode) {
                    throw new WebApiException(
                        "Ошибка при получении токена доступа.",
                        GetAsyncResult(tokenResponse.Content.ReadAsStringAsync())
                    );
                }

                var tokenDto = GetAsyncResult(tokenResponse.Content.ReadAsAsync<TokenDto>());

                _token = tokenDto.Token;
                _tokenExpires = DateTime.Now.AddSeconds(tokenDto.Lifetime - 60);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            return request;
        }        

        #endregion
    }
}