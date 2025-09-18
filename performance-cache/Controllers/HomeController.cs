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
    public class HomeController : ControllerBase
    {

        private const string key = "get-users";
        private const string redisConnection = "localhost:6379";
        private const string connectionString = "Server=localhost;database=fiap;User=root;Password=123";

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            //Implementar o cachê 
            var redis = ConnectionMultiplexer.Connect(redisConnection);
            IDatabase db = redis.GetDatabase();
            await db.KeyExpireAsync(key, TimeSpan.FromMinutes(20));
            string userValue = await db.StringGetAsync(key);

            if (!string.IsNullOrEmpty(userValue))
            {
                return Ok(userValue);
            }

            //Buscando no banco
            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            string sql = "select id, name, email from users; ";
            var users = await connection.QueryAsync<Users>(sql);
            var usersJson = JsonConvert.SerializeObject(users);
            await db.StringSetAsync(key, usersJson); //configura o cachê


            Thread.Sleep(3000); //forçando uma espera 
            return Ok(users);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody]Users user)
        {
            if (user == null) 
            {
                return BadRequest("Dados invalidos");
            }

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            string sql = @"
                insert into users(name, email)
                values(@Name, @Email);
                select last_insert_id();
            ";
            
            var newId = await connection.QuerySingleAsync<int>(sql, user);
            user.Id = newId;

            //Invalidar o cache anterior
            await InvalidateCache();

            return CreatedAtAction(nameof(Get), new { id = newId }, user);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(int id, [FromBody] Users user)
        {
            if (user == null)
            {
                return BadRequest("Usuário não fornecido.");
            }

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            user.Id = id;

            // Corrigindo a consulta SQL, adicionando a vírgula e tratando a palavra reservada 'user'
            string sql = @"
            UPDATE `users`
            SET
            name = @Name,
            email = @Email
            WHERE id = @Id;
            ";

            var rowsAffected = await connection.ExecuteAsync(sql, user);

            if (rowsAffected == 0)
            {
                return NotFound("Nenhum usuário encontrado!");
            }

            // Invalidar o cache
            await InvalidateCache();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (id == null)
            {
                return BadRequest("Usuário não fornecido.");
            }

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            string sql = @"
                delete from `users`
                where id = @Id;
            ";

            var rowAffected = await connection.ExecuteAsync(sql, new {id});
            await InvalidateCache();

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
