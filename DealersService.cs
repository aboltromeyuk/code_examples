using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Topshelf.Logging;
using System.Threading.Tasks;
using System.Reflection;
using System.Data.Entity;
using System.Net.Http;

namespace DMTool.WindowsService.Services
{
    /// <summary>
    /// Service for updating 
    /// dealers information
    /// </summary>
    public class DealersService : WebApiClientServiceBase, IDealersService
    {
        private readonly LogWriter _logger;

        private readonly IRepository<Dealer> _dealerRepository;
        private readonly IRepository<City> _cityRepository;
        private readonly IRepository<ConnProject> _connProjectsRepository;

        private static List<Type> _typesWithDealer { get; set; }

        private readonly string _cityUrl;
        private readonly string _showroomUrl;

        private static List<Type> TypesWithDealer
        {
            get
            {
                if (_typesWithDealer == null)
                {
                    _typesWithDealer = new List<Type>();

                    var _entitiesAssembly = Assembly.Load("DMTool.Models");
                    
                    var allTypeWithDealerProperty = _entitiesAssembly.GetTypes()
                        .Where(t => t.IsClass && typeof(IEntityBase).IsAssignableFrom(t) && t.GetProperties().Any(e => e.Name == "DealerId")).ToList();

                    var set = typeof(IDbSet<>);
                    foreach (var property in typeof(DMToolContext).GetProperties())
                    {
                        foreach (var type in allTypeWithDealerProperty)
                        {
                            //if (property.PropertyType == set.MakeGenericType(type))
                            if ((property.Name == type.Name || property.Name == type.Name + "s") && type.Name != "CampaignRegistration")
                            {
                                _typesWithDealer.Add(type);
                            }
                        }
                    }
                }
                return _typesWithDealer;
            }
        }

        private static string _command { get; set; }
        private static string Command
        {
            get
            {
                if (_command == null)
                {
                    var command = new StringBuilder("Declare @oldDealerId int, @newDealerId int; set @oldDealerId = {0}; set @newDealerId={1};");

                    foreach (var type in TypesWithDealer)
                    {
                        command.Append($"update {type.Name}s set DealerId=@newDealerId where DealerId=@oldDealerId;");
                    }

                    _command = command.ToString();
                }
                return _command;
            }
        }

        public DealersService(
           LogWriter logger,
            IRepository<Dealer> dealerRepository,
            IRepository<City> cityRepository,
            IRepository<ConnProject> connProjectsRepository,

            string cityUrl,
            string showroomUrl
        )
        {
            _logger = logger;
            _dealerRepository = dealerRepository;
            _cityRepository = cityRepository;
            _connProjectsRepository = connProjectsRepository;

            _cityUrl = cityUrl;
            _showroomUrl = showroomUrl;
        }

        public void SyncCities()
        {
            var citiesFromApi = Send<IEnumerable<CityDto>>(
                       HttpMethod.Get,
                       _cityUrl);

            // Del cities that do not exist
            var currCityIds = _cityRepository.GetAll().Select(c => c.Id).ToArray();
            var getCityIds = citiesFromApi.Select(c => c.Id).ToArray();
            var citiesForDelete = currCityIds.Except(getCityIds).ToArray();
            if (citiesForDelete.Any())
            {
                var dealers = _dealerRepository.GetQuery().Where(x => citiesForDelete.Contains((int)x.CityId)).ToList();
                foreach (var dealer in dealers)
                {
                    dealer.CityId = null;
                    _dealerRepository.Update(dealer);
                }

                _dealerRepository.SaveChanges();

                _cityRepository.BatchDelete(x => citiesForDelete.Contains(x.Id));
                _cityRepository.SaveChanges();
            }            

            foreach (var city in citiesFromApi)
            {
                try
                {
                    var dbCity = _cityRepository.FirstOrDefault(x => x.Id == city.Id);
                    if (dbCity != null)
                    {
                        dbCity.Name = city.Name;                        
                    }
                    else
                    {
                        var t = new City
                        {
                            Id = city.Id,
                            Name = city.Name,
                            SortOrder = 0                            
                        };

                        _cityRepository.Add(t);

                    }

                    _cityRepository.SaveChanges();
                }
                catch (Exception e)
                {
                    _logger.ErrorFormat("SyncCity error({0}) - {1}", city.Id, e);
                }
            }
        }

        public void SyncShowRooms()
        {
            SyncCities();
            
            var dealersFromApi = Send<IEnumerable<ShowRoomDto>>(
                       HttpMethod.Get,
                       _showroomUrl);

            int nextDealerId;

            foreach (var apiDealer in dealersFromApi)
            {
                try
                {
                    var dbDealer = _dealerRepository.FirstOrDefault(x => x.Gssn == apiDealer.Gssn);
                    
                    if (dbDealer == null)
                    {
                        var busy = _dealerRepository.FirstOrDefault(apiDealer.Id);

                        //If Id is occupied by other dealers, then we move it with all references
                        if (busy != null && busy.Id == apiDealer.Id)
                        {
                            nextDealerId = GetNewId();
                            MoveDealer(busy, nextDealerId);
                        }

                        CreateDealer(apiDealer);

                        //Add dealer records to all entities
                        CreateNavigationProperties(apiDealer.Id);
                    }
                    else
                    {
                        // If Id does not match
                        if (dbDealer.Id != apiDealer.Id)
                        {
                            var busy = _dealerRepository.FirstOrDefault(apiDealer.Id);
                            // If this id has other dealer
                            if (busy != null)
                            {      
                                //Move dealer with this id                        
                                nextDealerId = GetNewId();
                                MoveDealer(busy, nextDealerId);
                            }
                            
                            CreateDealer(apiDealer);

                            var newDealer = _dealerRepository.FirstOrDefault(apiDealer.Id);

                            //Move ref with oldId on newId
                            MoveDealerRealtion(dbDealer.Id, apiDealer.Id);

                            //Del dealer with old id
                            var delDealers = _dealerRepository.GetAll().Where(d => d.Id == dbDealer.Id).ToList();

                            foreach (var delDealer in delDealers)
                                _dealerRepository.Delete(delDealer);

                            _dealerRepository.SaveChanges();
                        }
                        else
                        {
                            if (dbDealer.Name != apiDealer.Name || dbDealer.Id != apiDealer.Id ||
                                dbDealer.Name != apiDealer.Name || dbDealer.CityId != apiDealer.CityId ||
                                dbDealer.Address != apiDealer.Address || dbDealer.CoFiCo != apiDealer.Cofico ||
                                dbDealer.Fax != apiDealer.Fax || dbDealer.Gssn != apiDealer.Gssn ||
                                Math.Abs(dbDealer.Latitude - apiDealer.Latitude) > 0.01 ||
                                Math.Abs(dbDealer.Longitude - apiDealer.Longitude) > 0.01 ||
                                dbDealer.Phone != apiDealer.Phone || dbDealer.Url != apiDealer.Site)
                            {
                                dbDealer.Name = apiDealer.Name;
                                dbDealer.Id = apiDealer.Id;
                                dbDealer.Name = apiDealer.Name;
                                dbDealer.CityId = apiDealer.CityId;
                                dbDealer.Address = apiDealer.Address;
                                dbDealer.CoFiCo = apiDealer.Cofico ?? "0";
                                dbDealer.Fax = apiDealer.Fax;
                                dbDealer.Gssn = apiDealer.Gssn;
                                dbDealer.Latitude = apiDealer.Latitude;
                                dbDealer.Longitude = apiDealer.Longitude;
                                dbDealer.Phone = apiDealer.Phone;
                                dbDealer.Url = apiDealer.Site;
                                
                                _dealerRepository.SaveChanges();
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.ErrorFormat("SyncDealer error({0}) - {1}", apiDealer.Id, e);
                }
            }

            try
            {
                var dealerFromApiIds = dealersFromApi.Select(x => x.Id).ToList();
                using (var context = new DMToolContext())
                {
                    var dealerToDelete = context.Dealers.Where(x => !x.IsArchive && !dealerFromApiIds.Contains(x.Id)).ToList();
                    if (dealerToDelete.Any())
                    {
                        dealerToDelete.ForEach(delaer =>
                        {
                            delaer.IsArchive = true;                            
                        });
                        context.SaveChanges();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorFormat("SyncDealer error delete - {0}", ex.ToString());
            }
        }

        private int GetNewId()
        {
            return _dealerRepository.GetQuery().Select(x => x.Id).OrderByDescending(x => x).FirstOrDefault() + 1;
        }
        private void MoveDealer(Dealer dbDealer, int newDealerId)
        {            
            var newDealer = new Dealer
            {
                Id = newDealerId,
                Name = dbDealer.Name,
                CityId = dbDealer.CityId,
                Address = dbDealer.Address,
                CoFiCo = dbDealer.CoFiCo,
                Fax = dbDealer.Fax,
                Gssn = dbDealer.Gssn,
                Latitude = dbDealer.Latitude,
                Longitude = dbDealer.Longitude,
                Phone = dbDealer.Phone,
                Url = dbDealer.Url,
                IsArchive = dbDealer.IsArchive,        
            };
            _dealerRepository.Add(newDealer);
            _dealerRepository.SaveChanges();

            MoveDealerRealtion(dbDealer.Id, newDealerId);

            var delDealers = _dealerRepository.GetAll().Where(d => d.Id == dbDealer.Id).ToArray();

            foreach(var item in delDealers)
                _dealerRepository.Delete(item);

            _dealerRepository.SaveChanges();
        }

        private void CreateDealer(ShowRoomDto apiDealer, int id = 0)
        {
            if (_cityRepository.GetQuery().Any(c => c.Id == apiDealer.CityId))
            {
                
                _dealerRepository.Add(new Dealer
                {
                    Id = id == 0 ? apiDealer.Id : id,
                    Name = apiDealer.Name,
                    CityId = apiDealer.CityId,
                    Address = apiDealer.Address,
                    CoFiCo = apiDealer.Cofico,
                    Fax = apiDealer.Fax,
                    Gssn = apiDealer.Gssn,                    
                    Latitude = apiDealer.Latitude,
                    Longitude = apiDealer.Longitude,
                    Phone = apiDealer.Phone,
                    Url = apiDealer.Site                    
                    //ToType = _dealerRepository.GetAll().Any(d => d.GSSN == apiDealer.Gssn) ? _dealerRepository.First(d => d.GSSN == apiDealer.Gssn).ToType : DealerToTypes.Other
                });                

                //_connProjectsRepository.Add(new ConnProject {
                //     Id = apiDealer.Id
                //});                
            }
            else
                throw new Exception("CityId = " + apiDealer.CityId + " not exists");

            _dealerRepository.SaveChanges();
           
            //_connProjectsRepository.SaveChanges();
        }

        private void MoveDealerRealtion(int oldDealerId, int newDealerId)
        {
            using (var context = new DMToolContext())
            {
                try
                {
                    context.Database.ExecuteSqlCommand(string.Format(Command, oldDealerId, newDealerId));

                    var oldconProject = context.ConnProjects.FirstOrDefault(c => c.Id == oldDealerId);

                    var oldcampReg = context.CampaignRegistrations.Where(c => c.DealerId == oldDealerId).ToList();

                    if (oldconProject != null)
                    {
                        context.ConnProjects.Remove(oldconProject);
                        context.SaveChanges();

                        context.ConnProjects.Add(new ConnProject
                        {
                            Id = newDealerId,
                            CompleteWheels = oldconProject.CompleteWheels,
                            ServiceContract = oldconProject.ServiceContract,
                            ServiceOnline = oldconProject.ServiceOnline,
                            ServicePrice = oldconProject.ServicePrice
                        });
                    }
                    else
                    {
                        context.ConnProjects.Add(new ConnProject
                        {
                            Id=newDealerId
                        });
                    }                    

                    if (oldcampReg != null)
                    {
                        context.CampaignRegistrations.RemoveRange(oldcampReg);
                        context.SaveChanges();

                        foreach(var item in oldcampReg)
                        {
                            context.CampaignRegistrations.Add(new CampaignRegistration
                            {
                                DealerId = newDealerId,
                                CampaignId = item.CampaignId,
                                Status = item.Status
                            });
                        }
                        
                    }                    

                    context.SaveChanges();
                }
                catch(Exception e)
                {
                    _logger.ErrorFormat("SyncDealer error in MoveDealerRealtion - {0}", e.ToString());
                }
                                
            }
        }

        private async void CreateNavigationProperties(int dealerId)
        {
            using (var context = new DMToolContext())
            {
                try
                {
                    foreach (var type in TypesWithDealer)
                    {
                        if (type.GetProperty("Year") == null)
                        {                            
                            var creat = Activator.CreateInstance(type);
                            creat.GetType().GetProperty("DealerId").SetValue(creat, dealerId);

                            context.Set(type).Add(creat);                            
                        }
                        else
                        {
                            var entities = context.Set(type);
                            var enList = await entities.SqlQuery("SELECT * FROM " + type.Name + "s").ToListAsync();
                            var years = enList.Select(e => e.GetType().GetProperty("Year").GetValue(e)).Distinct();                            

                            foreach (var year in years)
                            {
                                var creat = Activator.CreateInstance(type);

                                creat.GetType().GetProperty("DealerId").SetValue(creat, dealerId);
                                creat.GetType().GetProperty("Year").SetValue(creat, year);

                                context.Set(type).Add(creat);
                            }                            
                        }
                    }

                    context.SaveChanges();
                }
                catch(Exception e)
                {
                    _logger.ErrorFormat("SyncDealer error create navigation property - {0}", e.ToString());
                }                
            }                
        }        
    }
}

