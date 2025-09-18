using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Newtonsoft.Json;
using performance_cache.Model;
using StackExchange.Redis;
using System.Threading.Tasks;

namespace performance_cache.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VehicleController : ControllerBase
    {
        private const string key = "get-vehicles";
        private const string redisConnection = "localhost:6379";
        private const string connectionString = "Server=localhost;database=fiap;User=root;Password=123";

        // GET: api/vehicle
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            // Implementando o cache
            var redis = ConnectionMultiplexer.Connect(redisConnection);
            IDatabase db = redis.GetDatabase();
            await db.KeyExpireAsync(key, TimeSpan.FromMinutes(20));
            string vehicleValue = await db.StringGetAsync(key);

            if (!string.IsNullOrEmpty(vehicleValue))
            {
                return Ok(vehicleValue);
            }

            // Buscando no banco de dados
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            string sql = "SELECT id, brand, model, year, plate FROM vehicle;";
            var vehicles = await connection.QueryAsync<Vehicle>(sql);
            var vehiclesJson = JsonConvert.SerializeObject(vehicles);
            await db.StringSetAsync(key, vehiclesJson); // Configura o cache

            return Ok(vehicles);
        }

        // POST: api/vehicle
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] Vehicle vehicle)
        {
            if (vehicle == null)
            {
                return BadRequest("Dados inválidos.");
            }

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            string sql = @"
                INSERT INTO vehicle (brand, model, year, plate)
                VALUES (@Brand, @Model, @Year, @Plate);
                SELECT LAST_INSERT_ID();
            ";

            var newId = await connection.QuerySingleAsync<int>(sql, vehicle);
            vehicle.Id = newId;

            // Invalidar o cache anterior
            await InvalidateCache();

            return CreatedAtAction(nameof(Get), new { id = newId }, vehicle);
        }

        // PUT: api/vehicle/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Put(int id, [FromBody] Vehicle vehicle)
        {
            if (vehicle == null)
            {
                return BadRequest("Veículo não fornecido.");
            }

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            vehicle.Id = id;

            // Atualizando os dados do veículo
            string sql = @"
            UPDATE vehicle
            SET
                brand = @Brand,
                model = @Model,
                year = @Year,
                plate = @Plate
            WHERE id = @Id;
            ";

            var rowsAffected = await connection.ExecuteAsync(sql, vehicle);

            if (rowsAffected == 0)
            {
                return NotFound("Nenhum veículo encontrado!");
            }

            // Invalidar o cache
            await InvalidateCache();

            return NoContent();
        }

        // DELETE: api/vehicle/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (id == null)
            {
                return BadRequest("ID do veículo não fornecido.");
            }

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            string sql = @"
                DELETE FROM vehicle
                WHERE id = @Id;
            ";

            var rowsAffected = await connection.ExecuteAsync(sql, new { id });
            await InvalidateCache();

            if (rowsAffected == 0)
            {
                return NotFound("Veículo não encontrado.");
            }

            return NoContent();
        }

        private async Task InvalidateCache()
        {
            var redis = ConnectionMultiplexer.Connect(redisConnection);
            IDatabase db = redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
    }
}