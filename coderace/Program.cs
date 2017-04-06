using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using Newtonsoft.Json;

namespace Client
{    
    class Program
    {        
        public const string API_KEY = "E35413DE-47E0-4761-8D2D-F84E3F499187";

        public const int TICK_INTERVAL = 200;

        private static int m_CurrentRaceId;
        private static int m_CurrentRaceTick;
                      
        private static PlayerAction m_NextAction = new PlayerAction();
        private static object m_StateLock = new object();        

        static void Main(string[] args)
        {            
            //var timer = new Timer();
            while (true) {
                System.Threading.Thread.Sleep(TICK_INTERVAL);

                PlayerAction nextPost = null;
                lock (m_StateLock) { nextPost = m_NextAction.Clone(); }
                
                Task.Factory.StartNew(async () =>
                {
                    var response = await PlayerPost(nextPost);
                    if (response == null) return;

                    bool validPacket = false;                                 
                    lock (m_StateLock)
                    {                        
                        if (response.RaceId != m_CurrentRaceId)
                        {
                            m_CurrentRaceId = response.RaceId;
                            m_CurrentRaceTick = 0;
                        }                            

                        if (response.RaceTick > m_CurrentRaceTick)
                        {
                            m_CurrentRaceTick = response.RaceTick;
                            validPacket = true;
                        }
                    }

                    if (validPacket)
                    {
                        var action = PlayerUpdate(response);
                        lock (m_StateLock) { m_NextAction = action; }
                    }
                });                
            };
        }

        private static List<double> _lastVelocities = new List<double>();
        private static int _panicModeStartFrame = -1;

        private static PlayerAction PlayerUpdate(SensoryData sensory)
        {
            Console.WriteLine($"RaceId: {sensory.RaceId} RaceTick: {sensory.RaceTick}");
            var action = new PlayerAction()
            {
                throttle = 0,
                brake = 0,
                steer = 0,
                reverse = false
            };

            // steering
            var steer = sensory.nextWaypointDirection / 180;
            steer *= 2;
            if (steer < -1) steer = -1;
            if (steer > 1) steer = 1;
            action.steer = steer;

            var velocity = Math.Sqrt(Math.Abs(sensory.Velocity.X) * Math.Abs(sensory.Velocity.X) + Math.Abs(sensory.Velocity.Y) * Math.Abs(sensory.Velocity.Y));
            _lastVelocities.Add(velocity);
            Console.WriteLine($"vel: {velocity} next dir: {sensory.nextWaypointDirection}");

            // throttling
            var throttle = 0.1;

            if (_lastVelocities.Count >= 10 && _lastVelocities.All(v => v < 0.5)) {
                // panic
                _panicModeStartFrame = sensory.RaceTick; 
            }

            if (_panicModeStartFrame > 0) {
                Console.WriteLine("PANIC, let's reverse");
                action.reverse = true;
                if (sensory.RaceTick - _panicModeStartFrame > 10) {
                    _panicModeStartFrame = -1;
                }
                throttle = 0.3;
                action.steer = 0;
            }
            else {
                if (sensory.nextCornerWaypointDistance > 30 && -0.3 < sensory.nextCornerWaypointDirection && sensory.nextCornerWaypointDirection > 0.3) {
                    throttle = 0.5;
                } else {
                    throttle = 0.1;
                }
            }
            
            if (_lastVelocities.Count > 10)
                _lastVelocities.RemoveAt(0);
            
            action.throttle = throttle;

            Console.WriteLine($"Steer: {action.steer}, Throttle: {action.throttle}, Reverse: {action.reverse}");
            return action;
        }        

        private static async Task<SensoryData> PlayerPost(PlayerAction action)
        {
            try
            {                
                if (API_KEY == "PLACE API KEY HERE")
                    throw new Exception("Please write your API_KEY to const");                         
                             
                string contentJson = JsonConvert.SerializeObject(action);
                var content = new StringContent(contentJson, Encoding.UTF8, "application/json");

                using (var client = new HttpClient())                
                using (var response = await client.PostAsync($"http://coderacewebapi.azurewebsites.net/api/v1/player-update/{API_KEY}", content))
                {
                    string responseString = await response.Content.ReadAsStringAsync();

                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                            return JsonConvert.DeserializeObject<SensoryData>(responseString);
                        case HttpStatusCode.RequestTimeout:
                            return null;
                        default:
                            throw new Exception($"{response.StatusCode} => {responseString}");
                    }                                                                                                                                               
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine($"Error: {ex.InnerExceptions.FirstOrDefault()?.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            return null;
        }        
    }

    #region DataObjects
    public class PlayerAction
    {
        public double throttle;
        public double brake;
        public double steer;
        public bool reverse;

        public PlayerAction Clone()
        {
            return new PlayerAction()
            {
                throttle = throttle,
                brake = brake,
                steer = steer,
                reverse = reverse
            };
        }
    }

    public class SensoryData
    {
        public int Id;
        public int RaceId;
        public int RaceTick;
        public double TimeElapsed;

        public int LapsCompleted;
        public double CurrentLapElapsed;

        public double Throttle;
        public double Brake;
        public double Steer;

        public double nextWaypointDirection;
        public double nextWaypointDistance;
        public double nextCornerWaypointDirection;
        public double nextCornerWaypointDistance;
        public string nextCornerDirection;
        public string afterNextCornerDirection;

        public double SidewaysPosition;

        public Vec2 Velocity;
        public Vec2 Direction;
        public Vec2 Acceleration;

        public double FrontSlipAngle;
        public double RearSlipAngle;

        public Dictionary<int, Thing> Competitors = new Dictionary<int, Thing>();
        public Dictionary<int, Thing> Obstacles = new Dictionary<int, Thing>();
    }

    public struct Vec2
    {
        public float X;
        public float Y;
    }

    public class Thing
    {
        public double Direction;
        public double Distance;
        public double Size;
        public string Kind;
    }
    #endregion
}
