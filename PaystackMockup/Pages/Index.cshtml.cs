using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace PaystackMockup.Pages
{
    public class IndexModel : PageModel
    {
        // Dependencies injected via constructor
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IndexModel> _logger;

        // Constructor to initialize the dependencies
        public IndexModel(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<IndexModel> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;

            // Retrieve the Paystack public key from configuration
            PaystackPublicKey = _configuration["Paystack:PublicKey"];
        }

        // Model property to bind form data from the Razor page
        [BindProperty]
        public PaystackPaymentRequest PaymentRequest { get; set; }

        // Property to store the plan code for subscriptions
        public string PlanCode { get; private set; }

        // Paystack public key, used for the client-side integration
        public string PaystackPublicKey { get; }

        // Handles GET requests to display the page
        public IActionResult OnGet()
        {
            return Page();
        }

        // Handles POST requests for creating a payment
        public async Task<IActionResult> OnPostCreatePayment()
        {
            _logger.LogInformation("Processing payment...");

            _logger.LogInformation("PaymentRequest.Recurring: {Recurring}", PaymentRequest.Recurring);

            // Check if the form data is valid
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Model state is invalid.");
                return Page();
            }

            // Create an HTTP client to make API requests
            var client = _httpClientFactory.CreateClient();
            var paystackSecretKey = _configuration["Paystack:SecretKey"];
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {paystackSecretKey}");

            // If the payment is recurring, handle subscription logic
            if (PaymentRequest.Recurring)
            {
                _logger.LogInformation("Recurring payment selected. Creating plan...");

                // Handle recurring payment (subscription)
                var planCode = await CreatePaystackPlan(client);
                if (!string.IsNullOrEmpty(planCode))
                {
                    // Set the plan code for use in the Razor page and stay on the same page to trigger the popup
                    PlanCode = planCode;
                    return Page();
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Failed to create subscription plan. Please try again.");
                    return Page();
                }
            }
            else
            {
                _logger.LogInformation("One-time payment selected. Initializing transaction...");

                // Handle one-time payment
                return await CreateOneTimePayment(client);
            }
        }

        // Method to create a Paystack plan for recurring payments (subscription)
        private async Task<string> CreatePaystackPlan(HttpClient client)
        {
            // Prepare the request content with the necessary data
            var requestContent = new StringContent(JsonConvert.SerializeObject(new
            {
                name = $"{PaymentRequest.Name} Subscription Plan",
                amount = PaymentRequest.Amount * 100, // Convert amount to kobo
                interval = "monthly",  // Set the interval as needed (e.g., daily, weekly, monthly, annually)
                currency = "ZAR"
            }), Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending request to create Paystack plan...");
            _logger.LogInformation("Request Content: {RequestContent}", requestContent);

            // Send the request to the Paystack API to create a plan
            var response = await client.PostAsync("https://api.paystack.co/plan", requestContent);

            if (response.IsSuccessStatusCode)
            {
                // If successful, read the response and extract the plan code
                var result = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Plan created successfully. Response: {Response}", result);

                var jsonResult = JsonConvert.DeserializeObject<dynamic>(result);
                return jsonResult.data.plan_code;
            }
            else
            {
                // If there is an error, log the error and return null
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create plan. Status Code: {StatusCode}. Error: {ErrorContent}", response.StatusCode, errorContent);
                ModelState.AddModelError(string.Empty, $"Failed to create plan: {errorContent}");
                return null;
            }
        }

        // Method to initialize a one-time payment
        private async Task<IActionResult> CreateOneTimePayment(HttpClient client)
        {
            // Prepare the request content with the necessary data
            var requestContent = new StringContent(JsonConvert.SerializeObject(new
            {
                email = PaymentRequest.Email,
                amount = PaymentRequest.Amount * 100,  // Convert amount to kobo
                metadata = new
                {
                    name = PaymentRequest.Name
                }
            }), Encoding.UTF8, "application/json");

            // Send the request to the Paystack API to initialize the transaction
            var response = await client.PostAsync("https://api.paystack.co/transaction/initialize", requestContent);

            if (response.IsSuccessStatusCode)
            {
                // If successful, read the response and extract the payment URL
                var result = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Payment initialized successfully. Response: {Response}", result);

                var jsonResult = JsonConvert.DeserializeObject<dynamic>(result);
                string paymentUrl = jsonResult.data.authorization_url;

                // Redirect the user to the payment page
                return Redirect(paymentUrl);
            }
            else
            {
                // If there is an error, log the error and stay on the same page
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Payment initialization failed. Status Code: {StatusCode}. Error: {ErrorContent}", response.StatusCode, errorContent);
                ModelState.AddModelError(string.Empty, $"Payment initialization failed: {errorContent}");
                return Page();
            }
        }
    }

    // Model class to hold the payment request data
    public class PaystackPaymentRequest
    {
        public string Email { get; set; }  // Email of the user making the payment
        public int Amount { get; set; }  // Amount to be paid, in kobo (1 naira = 100 kobo)
        public string Name { get; set; }  // Name of the user making the payment
        public bool Recurring { get; set; }  // Indicates whether the payment is recurring (subscription)
    }
}
