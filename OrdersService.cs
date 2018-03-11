using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Hosting;
using ServiceOnline.Models;
using ServiceOnline.ViewModels.FZ.Home;
using Car = ServiceOnline.Models.Car;
using Dealer = ServiceOnline.Models.Dealer;
using DealerSchedule = ServiceOnline.Models.DealerSchedule;
using InspectionOrder = ServiceOnline.Models.InspectionOrder;
using ServiceOrder = ServiceOnline.Models.ServiceOrder;
using ServiceType = ServiceOnline.Models.ServiceType;
using Newtonsoft.Json;
using System.Security.Policy;
using System.Web;
using System.Globalization;

namespace ServiceOnline.Services
{
    public class OrdersService : IOrdersService
    {
        private readonly Lazy<IRepository<Car, int>> _carsRepository;
        private readonly Lazy<IRepository<CarClass, int>> _carClassesRepository;
        private readonly Lazy<IRepository<City, int>> _citiesRepository;
        private readonly Lazy<IRepository<Dealer, int>> _dealersRepository;
        private readonly Lazy<IRepository<DealerSchedule, long>> _dealerSchedulesRepository;
        private readonly Lazy<IRepository<DealerConsultantSchedule, int>> _dealerConsultantSchedulesRepository;
        private readonly Lazy<IRepository<DealerConsultant, int>> _dealerConsultantsRepository;
        private readonly Lazy<IRepository<InspectionOrder, int>> _inspectionOrdersRepository;
        private readonly Lazy<IRepository<ServiceOrder, int>> _serviceOrdersRepository;
        private readonly Lazy<IRepository<ServiceType, int>> _serviceTypesRepository;
        private readonly Lazy<IRepository<Models.Banner, int>> _bannersRepository;

        public readonly string _dealerConsultantImgPath;
        public readonly string _baseUrl =  HttpContext.Current.Request.Url.Scheme + "://" + HttpContext.Current.Request.Url.Host;

        public OrdersService(
            Lazy<IRepository<Car, int>> carsRepository,
            Lazy<IRepository<CarClass, int>> carClassesRepository,
            Lazy<IRepository<City, int>> citiesRepository,
            Lazy<IRepository<Dealer, int>> dealersRepository,
            Lazy<IRepository<DealerSchedule, long>> dealerSchedulesRepository,
            Lazy<IRepository<DealerConsultantSchedule, int>> dealerConsultantSchedulesRepository,
            Lazy<IRepository<DealerConsultant, int>> dealerConsultantsRepository,
            Lazy<IRepository<InspectionOrder, int>> inspectionOrdersRepository,
            Lazy<IRepository<ServiceOrder, int>> serviceOrdersRepository,
            Lazy<IRepository<ServiceType, int>> serviceTypesRepository,
            Lazy<IRepository<Models.Banner, int>> bannersRepository,
            string dealerConsultantImgPath
        )
        {
            _carsRepository = carsRepository;
            _carClassesRepository = carClassesRepository;
            _citiesRepository = citiesRepository;
            _dealersRepository = dealersRepository;
            _dealerSchedulesRepository = dealerSchedulesRepository;
            _dealerConsultantSchedulesRepository = dealerConsultantSchedulesRepository;
            _dealerConsultantsRepository = dealerConsultantsRepository;
            _inspectionOrdersRepository = inspectionOrdersRepository;
            _serviceOrdersRepository = serviceOrdersRepository;
            _serviceTypesRepository = serviceTypesRepository;
            _bannersRepository = bannersRepository;
            _dealerConsultantImgPath = dealerConsultantImgPath.TrimEnd('/') + "/";
        }

        public IEnumerable<ViewModels.FZ.Home.Car> GetCars(int carClassId)
        {
            return _carsRepository.Value.GetQuery()
                .Where(x => x.CarClassId == carClassId && !string.IsNullOrEmpty(x.ManufactureYearsInternal))
                .OrderBy(x => x.Modification)
                .AsEnumerable()
                .Select(x => new ViewModels.FZ.Home.Car
                {
                    Id = x.Id,
                    Modification = x.Modification,
                    ManufactureYears = x.ManufactureYears
                });
        }

        public IEnumerable<ViewModels.FZ.Home.ServiceType> GetServiceTypes(int? dealerId)
        {
            var isOtherToType = dealerId == null || _dealersRepository.Value.First(dealerId.Value).ToType == DealerToTypes.Other;

            if (!_serviceTypesRepository.Value.GetQuery().Any(x => x.DealerId == dealerId))
            {
                dealerId = null;
            }

            return _serviceTypesRepository.Value.GetQuery()
                .Where(x => x.DealerId == dealerId && !(isOtherToType && x.IsInspection))
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .Select(x => new ViewModels.FZ.Home.ServiceType
                {
                    Id = x.Id,
                    Name = x.Name
                });
        }

        public IDictionary<string, IEnumerable<ViewModels.FZ.Home.DealerSchedule>> GetDealerSchedule(int dealerId, string clientDateTime)
        {
            var schedule = _dealerSchedulesRepository.Value.GetQuery()
                .Where(x => x.DealerId == dealerId)
                .OrderBy(x => x.IntervalFrom)
                .ToList();

            var result = new Dictionary<string, IEnumerable<ViewModels.FZ.Home.DealerSchedule>>();
            string convertetUserDateTime;
            var userDateTime = DateTime.Now;

            if (clientDateTime != null)
            {
                convertetUserDateTime = clientDateTime.Substring(0, clientDateTime.IndexOf("G")).Trim();
                userDateTime = DateTime.ParseExact(convertetUserDateTime,
                                  "ddd MMM dd yyyy HH:mm:ss",
                                  CultureInfo.InvariantCulture);
            }                

            for (var date = userDateTime.Date; date < userDateTime.Date + 31.Days(); date += 1.Days())
            {
                var values = schedule.Any()
                    ? schedule.Where(x => x.IntervalFrom >= date && x.IntervalFrom < date + 1.Days())
                    : GetDefaultDealerSchedule(date);

                values = values.Where(x => x.IntervalFrom > userDateTime);

                result.Add(
                    date.ToString("dd.MM.yyyy"),
                    values
                        .Select(x => new ViewModels.FZ.Home.DealerSchedule
                        {
                            IntervalFrom = x.IntervalFrom.TimeOfDay.ToString("hh\\:mm"),
                            IntervalAsString = x.TimeIntervalAsString,
                            IsFree = x.IsFree,
                            IsSpecialPrice = x.IsSpecialPrice,
                            Comment = x.Comment
                        })
                        .ToList()
                );
            }

            return result;
        }

        public IDictionary<string, Consultant> GetConsultants(int dealerId)
        {
            var consultants = _dealerConsultantsRepository.Value.GetQuery()
                .Where(x => x.DealerId == dealerId).ToList();

            var result = new Dictionary<string, Consultant>();

            foreach(var consultant in consultants)
            {
                result.Add(consultant.Id.ToString(), new Consultant { Name = $"{consultant.Firstname} {consultant.Surname}" });
            }

            return result;            
        }       

        public IDictionary<string, ConsultantSchedule> GetConsultantSchedule(int dealerId, string clientDateTime)
        {
            var consultants = _dealerConsultantsRepository.Value.GetQuery()
                .Where(x => x.DealerId == dealerId).ToList();

            var schedule = _dealerConsultantSchedulesRepository.Value.GetQuery()
                .Where(x => x.DealerConsultant.DealerId == dealerId)
                //.OrderBy(x => x.TimeFrom)
                .ToList();

            if (!schedule.Any())
            {
                return new Dictionary<string, ConsultantSchedule>();
            }

            string convertetUserDateTime;
            var userDateTime = DateTime.Now;

            if (clientDateTime != null)
            {
                convertetUserDateTime = clientDateTime.Substring(0, clientDateTime.IndexOf("G")).Trim();
                userDateTime = DateTime.ParseExact(convertetUserDateTime,
                                  "ddd MMM dd yyyy HH:mm:ss",
                                  CultureInfo.InvariantCulture);
            }

            var dateNow = userDateTime.Date;

            var inspectionOrders =
                _inspectionOrdersRepository.Value.GetQuery()
                    .Where(x =>
                            x.DealerId == dealerId && x.ToType == OrderToTypes.Consultant &&
                            x.OrderDateTime >= dateNow)
                    .ToList();

            var result = new Dictionary<string, ConsultantSchedule>();

            foreach (var consultant in consultants)
            {
                var dealerSchedule = new ConsultantSchedule();
                dealerSchedule.Name = $"{consultant.Firstname} {consultant.Surname}";
                dealerSchedule.WeekendWorkTimeFrom = consultant.WeekendWorkTimeFrom.Value.ToString("hh\\:mm");
                dealerSchedule.WeekendWorkTimeTo = consultant.WeekendWorkTimeTo.Value.ToString("hh\\:mm");
                dealerSchedule.WorkweekWorkTimeFrom = consultant.WorkweekWorkTimeFrom.Value.ToString("hh\\:mm");
                dealerSchedule.WorkweekWorkTimeTo = consultant.WorkweekWorkTimeTo.Value.ToString("hh\\:mm");
                dealerSchedule.IsTimetableHidden = consultant.IsTimetableHidden;
                dealerSchedule.Image = !string.IsNullOrWhiteSpace(consultant.ImageName) ? _dealerConsultantImgPath + consultant.ImageName
                    : ("/Content/Images/profile_user_flat.svg");

                for (var date = userDateTime.Date; date < userDateTime.Date + 31.Days(); date += 1.Days())
                {
                    var values = schedule.Any(x => x.DealerConsultantId == consultant.Id)
                        ? schedule.Where(x => x.DealerConsultantId == consultant.Id && x.Date >= date && x.Date < date + 1.Days())
                        : new List<DealerConsultantSchedule>();

                    values = values.Where(x => x.Date > userDateTime.Date || x.Date == userDateTime.Date && x.TimeFrom >= userDateTime.TimeOfDay);

                    var dealerOfConsultantShedule = _dealerSchedulesRepository.Value.Where(s => s.DealerId == consultant.DealerId).ToList();

                    dealerSchedule.ScheduleByDate.Add(
                        date.ToString("dd.MM.yyyy"),
                        values
                            .Select(x => new ViewModels.FZ.Home.DealerSchedule
                            {
                                IntervalFrom = x.TimeFrom.ToString("hh\\:mm"),
                                IntervalAsString = string.Format("{0:hh\\:mm} - {1:hh\\:mm}", x.TimeFrom, x.TimeTo),
                                IsFree = x.IsFree,
                                IsSpecialPrice = GetSpecPriceIdent(dealerOfConsultantShedule,date, x.TimeFrom, x.TimeTo).IsSpecPrice,
                                Comment = GetSpecPriceIdent(dealerOfConsultantShedule, date, x.TimeFrom, x.TimeTo).Comment
                            })
                            .ToList()
                    );
                }

                result.Add(consultant.Id.ToString(), dealerSchedule);
            }

            return result;
        }

        public IDictionary<string, IEnumerable<ViewModels.FZ.Home.DealerSchedule>> GetDealerScheduleWebApi(int dealerId, string clientDateTime)
        {
            var schedule = _dealerSchedulesRepository.Value.GetQuery()
                .Where(x => x.DealerId == dealerId)
                .OrderBy(x => x.IntervalFrom)
                .ToList();

            var result = new Dictionary<string, IEnumerable<ViewModels.FZ.Home.DealerSchedule>>();
            
            var userDateTime = DateTime.Now;

            if (clientDateTime != null)
                userDateTime = DateTime.Parse(clientDateTime);            

            for (var date = userDateTime.Date; date < userDateTime.Date + 31.Days(); date += 1.Days())
            {
                var values = schedule.Any()
                    ? schedule.Where(x => x.IntervalFrom >= date && x.IntervalFrom < date + 1.Days())
                    : GetDefaultDealerSchedule(date);

                values = values.Where(x => x.IntervalFrom > userDateTime);

                result.Add(
                    date.ToString("dd.MM.yyyy"),
                    values
                        .Select(x => new ViewModels.FZ.Home.DealerSchedule
                        {
                            IntervalFrom = x.IntervalFrom.TimeOfDay.ToString("hh\\:mm"),
                            IntervalAsString = x.TimeIntervalAsString,
                            IsFree = x.IsFree,
                            IsSpecialPrice = x.IsSpecialPrice,
                            Comment = x.Comment
                        })
                        .ToList()
                );
            }

            return result;
        }

        public IDictionary<string, ConsultantSchedule> GetConsultantScheduleWebApi(int dealerId, string clientDateTime)
        {
            var consultants = _dealerConsultantsRepository.Value.GetQuery()
                .Where(x => x.DealerId == dealerId).ToList();

            var schedule = _dealerConsultantSchedulesRepository.Value.GetQuery()
                .Where(x => x.DealerConsultant.DealerId == dealerId)
                //.OrderBy(x => x.TimeFrom)
                .ToList();

            if (!schedule.Any())
            {
                return new Dictionary<string, ConsultantSchedule>();
            }
            
            var userDateTime = DateTime.Now;

            if (clientDateTime != null)
                userDateTime = DateTime.Parse(clientDateTime);            

            var dateNow = userDateTime.Date;

            var inspectionOrders =
                _inspectionOrdersRepository.Value.GetQuery()
                    .Where(x =>
                            x.DealerId == dealerId && x.ToType == OrderToTypes.Consultant &&
                            x.OrderDateTime >= dateNow)
                    .ToList();

            var result = new Dictionary<string, ConsultantSchedule>();

            foreach (var consultant in consultants)
            {
                var dealerSchedule = new ConsultantSchedule();
                dealerSchedule.Name = $"{consultant.Firstname} {consultant.Surname}";
                dealerSchedule.WeekendWorkTimeFrom = consultant.WeekendWorkTimeFrom.Value.ToString("hh\\:mm");
                dealerSchedule.WeekendWorkTimeTo = consultant.WeekendWorkTimeTo.Value.ToString("hh\\:mm");
                dealerSchedule.WorkweekWorkTimeFrom = consultant.WorkweekWorkTimeFrom.Value.ToString("hh\\:mm");
                dealerSchedule.WorkweekWorkTimeTo = consultant.WorkweekWorkTimeTo.Value.ToString("hh\\:mm");
                dealerSchedule.Image = !string.IsNullOrWhiteSpace(consultant.ImageName) ? _baseUrl + _dealerConsultantImgPath + consultant.ImageName
                    : _baseUrl + "/Content/Images/profile_user_flat.png";
                dealerSchedule.IsTimetableHidden = consultant.IsTimetableHidden;

                for (var date = DateTime.Today; date < DateTime.Today + 31.Days(); date += 1.Days())
                {
                    var values = schedule.Any(x => x.DealerConsultantId == consultant.Id)
                        ? schedule.Where(x => x.DealerConsultantId == consultant.Id && x.Date >= date && x.Date < date + 1.Days())
                        : new List<DealerConsultantSchedule>();

                    values = values.Where(x => x.Date > DateTime.Now.Date || x.Date == DateTime.Now.Date && x.TimeFrom >= DateTime.Now.TimeOfDay);


                    dealerSchedule.ScheduleByDate.Add(
                        date.ToString("dd.MM.yyyy"),
                        values
                            .Select(x => new ViewModels.FZ.Home.DealerSchedule
                            {
                                IntervalFrom = x.TimeFrom.ToString("hh\\:mm"),
                                IntervalAsString = string.Format("{0:hh\\:mm} - {1:hh\\:mm}", x.TimeFrom, x.TimeTo),
                                //dont use Resharper here please
                                //dont use Resharper here please
                                //IsFree = inspectionOrders.Any()
                                //&& inspectionOrders.Any(y => y.DealerId == dealerId && y.DealerConsultantCode == consultant.Code && y.OrderDateTime == (x.Date + x.TimeFrom)) ? false : x.IsFree,
                                IsFree = x.IsFree,
                                IsSpecialPrice = false,
                                Comment = ""
                            })
                            .ToList()
                    );
                }

                result.Add(consultant.Id.ToString(), dealerSchedule);
            }

            return result;
        }

        public IDictionary<string, object> GetSchedule(int dealersShowroomId, string clientDateTime)
        {
            var dealer = _dealersRepository.Value.SingleOrDefault(d => d.DealersShowroomId == dealersShowroomId);

            if (dealer == null || dealer.ToType == DealerToTypes.Other)
                return null;

            if(dealer.ToType == DealerToTypes.CarLift)
                return GetDealerScheduleWebApi(dealer.Id, clientDateTime).ToDictionary(s=>s.Key, s=>(object)s.Value);
            else 
                return GetConsultantScheduleWebApi(dealer.Id, clientDateTime).ToDictionary(s => s.Key, s => (object)s.Value);
        }

        public IEnumerable<ViewModels.WebApi.ShowRoom> GetShowroomsByDealersDealerId(int dealerId)
        {
            return _dealersRepository.Value
                .Where(x => !x.HideOnFront && x.DealersDealerId == dealerId && x.DealersShowroomId != null)
                .Select(x => new ViewModels.WebApi.ShowRoom
                {
                    Id = x.DealersShowroomId.Value,
                    Name = x.Name,
                    City = x.City.Name,
                    Address = x.Address,
                    ServicePhone = x.Phone,
                    DaysBeforeStartAccepting = 0,
                    DaysBeforeEndAccepting = 31,
                    TimeBeforeClosing = 0,
                    GetRepairStatus = false,
                    NotificationsEmail = x.NotificationsEmail
                })
                .ToArray();
        }

        public IEnumerable<ViewModels.WebApi.ServiceType> GetShowroomServiceTypes(int? showroomId)
        {
            var serviceTypes = _dealersRepository.Value
                .Where(x => showroomId != null && x.DealersShowroomId == showroomId)
                .SelectMany(x => x.ServiceTypes);

            if (!serviceTypes.Any())
            {
                serviceTypes = _serviceTypesRepository.Value.Where(x => x.DealerId == null);
            }

            return serviceTypes
                .Select(x => new ViewModels.WebApi.ServiceType
                {
                    Id = x.Id,
                    Name = x.Name
                })
                .ToArray();
        }

        public ViewModels.FZ.Home.Index FillDictionaries(ViewModels.FZ.Home.Index model)
        {
            model.CarClassesDictionary = _carClassesRepository.Value.GetQuery()
                .Where(a => !a.IsDeleted)
                .OrderBy(x => x.Name)
                .ToSelectList(x => x.Id, x => x.Name);

            model.ServiceTypesDictionary = _serviceTypesRepository.Value.GetQuery()
                .Where(x => !x.DealerId.HasValue)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .ToSelectList(x => x.Id, x => x.Name, x => x.Id == model.ServiceTypeId);

            model.CitiesDictionary = _citiesRepository.Value.GetQuery()
                .Where(x => x.Dealers.Any())
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .ToSelectList(x => x.Id, x => x.Name);

            model.DefaultDealerScheduleDictionary = GetDefaultDealerSchedule(DateTime.Now)
                .ToSelectList(x => x.IntervalFrom, x => x.TimeIntervalAsString);

            model.Dealers = _dealersRepository.Value.GetQuery()
                .Where(x => !x.HideOnFront && (string.IsNullOrEmpty(model.DealerDomain) || x.Domain == model.DealerDomain || x.Id == model.DealerDomainAsIdentity))
                .OrderBy(x => x.Name)
                .Select(x => new ViewModels.FZ.Home.Dealer
                {
                    Id = x.Id,
                    Name = x.Name,
                    CityId = x.CityId==null ? 0 : x.CityId.Value,
                    Address = x.Address,
                    Phone = x.Phone,
                    Fax = x.Fax,
                    Url = x.Url,
                    Latitude = x.Latitude,
                    Longitude = x.Longitude,
                    ToType = x.ToType
                })
                .ToArray();

            model.Banners = _bannersRepository.Value.GetQuery()
                .Select(b => new ViewModels.FZ.Home.Banner
                {
                    Name = b.Name,
                    Description = b.Description,
                    ImgPath = b.FileNameImg,
                    TextColor = b.TextColor
                })
                .ToList();

            return model;
        }

        public IEnumerable<object> GetCarClasses()
        {
            var result = _carClassesRepository.Value.GetQuery().Where(c=>!c.IsDeleted).Select(c => new { Id = c.Id, Name = c.Name, SysName = c.ContactsDBSysName }).ToList();

            return result;
        }

        public IEnumerable<object> GetCarModification(int id)
        {
            var result = _carsRepository.Value.GetQuery().Where(m => m.CarClassId == id).Select(m => new { Id = m.Id, Name = m.Modification }).ToList();

            return result;
        }

        public IEnumerable<int> GetCarManufactureYears(int id)
        {
            var result = _carsRepository.Value.GetQuery().FirstOrDefault(y => y.Id == id).ManufactureYears;

            return result;
        }

        public void CreateOrder(ViewModels.FZ.Home.InspectionOrder model)
        {
            DealerConsultant consultant = model.DealerConsultantId.HasValue ?
                _dealerConsultantsRepository.Value.GetQuery().FirstOrDefault(x => x.Id == model.DealerConsultantId.Value) : null;

            if (model.DealerConsultantId.HasValue && consultant == null)
            {
                throw new BusinessException("Данные страницы устарели. Пожалуйста перезагрузите сайт");
            }

            var order = new InspectionOrder
            {
                Gender = model.Gender,
                LastName = model.LastName,
                FirstName = model.FirstName,
                Patronymic = model.Patronymic,
                Phone = model.Phone,
                Email = model.Email,
                Comment = model.Comment,
                CarId = model.CarId ?? 0,
                CarManufactureYear = model.CarManufactureYear.Value,
                CarVin = model.CarVin,
                DealerConsultantCode = consultant?.Code,
                DealerConsultantName = consultant?.Firstname + " " + consultant?.Surname,
                ToType = model.ToType,
                DealerId = model.DealerId.Value,
                OrderDateTime = model.OrderDate.Value.Date + model.OrderTime.Value,
                CreatedDateTime = DateTime.Now,
                ModifiedDateTime = DateTime.Now,
                OrderSource = OrderSourceType.Desktop
            };

            _inspectionOrdersRepository.Value.Add(order);
            _inspectionOrdersRepository.Value.SaveChanges();

            _inspectionOrdersRepository.Value.ReferenceLoad(order, "Car", "Dealer");

            if (consultant == null)
            {
                _dealerSchedulesRepository.Value.First(s => s.DealerId == model.DealerId.Value && s.IntervalFrom == order.OrderDateTime).IsFree = false;
                _dealerSchedulesRepository.Value.SaveChanges();
            }

            else
            {
                _dealerConsultantSchedulesRepository.Value.First(s => s.DealerConsultant.Code == consultant.Code &&
                                                                                          DbFunctions.TruncateTime(s.Date) == model.OrderDate.Value && 
                                                                                          s.TimeFrom == model.OrderTime.Value).IsFree = false;
                _dealerConsultantSchedulesRepository.Value.SaveChanges();
            }                
        }        

        public void CreateOrder(ViewModels.FZ.Home.ServiceOrder model)
        {
            var order = new ServiceOrder
            {
                Gender = model.Gender,
                LastName = model.LastName,
                FirstName = model.FirstName,
                Patronymic = model.Patronymic,
                Phone = model.Phone,
                Email = model.Email,
                Comment = model.Comment,
                ServiceTypeId = model.ServiceTypeId.Value,
                CarClassId = model.CarClassId.Value,
                CarModification = model.CarModification,
                CarManufactureYear = model.CarManufactureYear.Value,
                CarVIN = model.CarVIN,
                CarRegistrationNumber = model.CarRegistrationNumber,
                DealerId = model.DealerId.Value,
                CreatedDateTime = DateTime.Now,
                ModifiedDateTime = DateTime.Now,
                OrderSource = OrderSourceType.Desktop
            };

            _serviceOrdersRepository.Value.Add(order);
            _serviceOrdersRepository.Value.SaveChanges();

            _serviceOrdersRepository.Value.ReferenceLoad(order, "ServiceType", "CarClass", "Dealer");
        }

        public void CreateOrder(ViewModels.WebApi.ServiceOrder model)
        {
            var carClass = _carClassesRepository.Value.FirstOrDefault(x => x.ContactsDBSysName == model.ClassSysName && !x.IsDeleted);
            if (carClass == null)
            {
                throw new Exception(string.Format("Класс автомобиля с ContactsDBSysName \"{0}\" не найден.", model.ClassSysName));
            }

            var dealer = _dealersRepository.Value.FirstOrDefault(x => x.DealersShowroomId == model.ShowroomId);
            if (dealer == null)
            {
                throw new Exception(string.Format("Дилер с DealersShowroomID \"{0}\" не найден.", model.ShowroomId));
            }            
                var order = new ServiceOrder
                {
                    Gender = model.Gender,
                    LastName = model.LastName,
                    FirstName = model.FirstName,
                    Patronymic = model.PatronymicName,
                    Phone = model.Phone,
                    Email = model.Email,
                    Comment = model.Comment,
                    ServiceTypeId = model.ServiceTypeId,
                    CarClassId = carClass.Id,
                    CarModification = model.CarModel,
                    CarManufactureYear = model.CarYearRelease,
                    CarVIN = model.CarVin,
                    CarRegistrationNumber = model.CarRegNumber,
                    DealerId = dealer.Id,
                    CreatedDateTime = DateTime.Now,
                    ModifiedDateTime = DateTime.Now,
                    CDBTransferDateTime = DateTime.Now,
                    OrderSource = OrderSourceType.Mobile                    
                };

                _serviceOrdersRepository.Value.Add(order);
                _serviceOrdersRepository.Value.SaveChanges();            
        }

        public void CreateOrder(string userGuid, ViewModels.WebApi.ServiceOrder model)
        {
            var carClass = _carClassesRepository.Value.FirstOrDefault(x => x.ContactsDBSysName == model.ClassSysName && !x.IsDeleted);
            if (carClass == null)
            {
                throw new Exception(string.Format("Класс автомобиля с ContactsDBSysName \"{0}\" не найден.", model.ClassSysName));
            }

            var dealer = _dealersRepository.Value.FirstOrDefault(x => x.DealersShowroomId == model.ShowroomId);
            if (dealer == null)
            {
                throw new Exception(string.Format("Дилер с DealersShowroomID \"{0}\" не найден.", model.ShowroomId));
            }

            var serviceType = _serviceTypesRepository.Value.FirstOrDefault(st => st.Id == model.ServiceTypeId);
            if (serviceType == null)
            {
                throw new Exception(string.Format("Вид работ с ID \"{0}\" не найден.", model.ServiceTypeId));
            }

            DealerConsultant consultant = model.DealerConsultantId.HasValue ?
                _dealerConsultantsRepository.Value.GetQuery().FirstOrDefault(x => x.Id == model.DealerConsultantId.Value) : null;

            if (model.DealerConsultantId.HasValue && consultant == null)
            {
                throw new Exception("Данные о консультантах устарели");
            }

            var car = _carsRepository.Value.FirstOrDefault(c => c.CarClassId == carClass.Id && c.Modification == model.CarModel);
            if (car == null)
            {
                throw new Exception(string.Format("Данная модификация автомобиля не найдена"));
            }            

            if (serviceType.IsInspection)
            {
                var order = new InspectionOrder
                {
                    Gender = model.Gender,
                    LastName = model.LastName,
                    FirstName = model.FirstName,
                    Patronymic = model.PatronymicName,
                    Phone = model.Phone,
                    Email = model.Email,
                    Comment = model.Comment,
                    CarId = car.Id,
                    CarManufactureYear = model.CarYearRelease,
                    CarVin = model.CarVin,
                    DealerConsultantCode = consultant?.Code,
                    DealerConsultantName = consultant?.Firstname + " " + consultant?.Surname,
                    ToType = (OrderToTypes)model.ToType.Value,
                    DealerId = dealer.Id,
                    OrderDateTime = DateTime.Parse(model.PreferableDate),
                    CreatedDateTime = DateTime.Now,
                    ModifiedDateTime = DateTime.Now,
                    OrderSource = OrderSourceType.Mobile,
                    UserGuid = userGuid                    
                };

                _inspectionOrdersRepository.Value.Add(order);
                _inspectionOrdersRepository.Value.SaveChanges();

                if (consultant == null)
                {
                    _dealerSchedulesRepository.Value.First(s => s.DealerId == dealer.Id && s.IntervalFrom == order.OrderDateTime).IsFree = false;
                    _dealerSchedulesRepository.Value.SaveChanges();
                }

                else
                {
                    _dealerConsultantSchedulesRepository.Value.First(s => s.DealerConsultant.Code == consultant.Code &&
                                                                                              DbFunctions.TruncateTime(s.Date) == order.OrderDateTime.Date &&
                                                                                              s.TimeFrom == order.OrderDateTime.TimeOfDay).IsFree = false;
                    _dealerConsultantSchedulesRepository.Value.SaveChanges();
                }
            }
            else
            {
                var order = new ServiceOrder
                {
                    Gender = model.Gender,
                    LastName = model.LastName,
                    FirstName = model.FirstName,
                    Patronymic = model.PatronymicName,
                    Phone = model.Phone,
                    Email = model.Email,
                    Comment = model.Comment,
                    ServiceTypeId = model.ServiceTypeId,
                    CarClassId = carClass.Id,
                    CarModification = model.CarModel,
                    CarManufactureYear = model.CarYearRelease,
                    CarVIN = model.CarVin,
                    CarRegistrationNumber = model.CarRegNumber,
                    DealerId = dealer.Id,
                    CreatedDateTime = DateTime.Now,
                    ModifiedDateTime = DateTime.Now,
                    CDBTransferDateTime = DateTime.Now,
                    OrderSource = OrderSourceType.Mobile,
                    UserGuid = userGuid
                };

                _serviceOrdersRepository.Value.Add(order);
                _serviceOrdersRepository.Value.SaveChanges();
            }            
        }

        private IEnumerable<DealerSchedule> GetDefaultDealerSchedule(DateTime date)
        {
            const int hourFrom = 9;
            const int hourTo = 23;

            return Enumerable.Range(hourFrom, hourTo - hourFrom)
                .Select(x => new DealerSchedule
                {
                    IntervalFrom = date.Date + x.Hours(),
                    IntervalDuration = 1.Hours(),
                    IsFree = true
                });
        }  

        /// <summary>
        /// Get info: has interval spec price or hasn't
        /// </summary>
        /// <param name="sealerShedule">dealer shedule</param>
        /// <param name="date">interval date</param>
        /// <param name="timeFrome">interval from</param>
        /// <param name="timeTo">interval to</param>
        /// <returns></returns>
        
        private SpecPriceIdent GetSpecPriceIdent(IEnumerable<DealerSchedule> dealerShedule, DateTime date, TimeSpan timeFrome, TimeSpan timeTo)
        {
            var result = new SpecPriceIdent { IsSpecPrice=false, Comment="" };

            var dealerSheduleDateArray = dealerShedule.Where(s => s.IntervalFrom.Date == date).ToArray();
            
            for(var i = 0; i<dealerSheduleDateArray.Length; i++)
            {
                if(dealerSheduleDateArray[i].IntervalFrom.TimeOfDay <= timeFrome && dealerSheduleDateArray[i].IsSpecialPrice)
                {
                    for(var j = i; dealerSheduleDateArray[j].IsSpecialPrice && j <dealerSheduleDateArray.Length; j++)
                    {
                        if(dealerSheduleDateArray[j].IntervalFrom.TimeOfDay + dealerSheduleDateArray[j].IntervalDuration >= timeTo)
                        {
                            result.IsSpecPrice = true;
                            result.Comment = dealerSheduleDateArray[j].Comment;
                        }
                    }
                }
                
            }

            return result;
        }      
    }

    class SpecPriceIdent
    {
        public bool IsSpecPrice { get; set; }
        public string Comment { get; set; }
    }
}
