using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace WpfApp2
{
    public partial class MainWindow : Window
    {
        private readonly HttpClient client = new HttpClient { BaseAddress = new Uri("http://localhost:4444") };

        private List<Movie> allMoviesCache = new List<Movie>();
        private List<Screening> allScreeningsCache = new List<Screening>();

        private readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        private string authToken = null;
        private User currentUser = null;
        private int? editingMovieId = null;

        public MainWindow()
        {
            InitializeComponent();
            InitializeApplicationState();
        }

        private async void InitializeApplicationState()
        {
            UpdateUIVisibility();
            if (RegisterPanel != null) RegisterPanel.Visibility = Visibility.Collapsed;
            if (AddMoviePanel != null) AddMoviePanel.Visibility = Visibility.Collapsed;
            if (AddScreeningPanel != null) AddScreeningPanel.Visibility = Visibility.Collapsed;
            if (CancelEditButton != null) CancelEditButton.Visibility = Visibility.Collapsed;

            await RefreshAllDataAsync();
        }

        private void UpdateUIVisibility()
        {
            bool isLoggedIn = currentUser != null;
            bool isAdmin = isLoggedIn && currentUser.IsAdmin;

            if (LoginPanel != null) LoginPanel.Visibility = isLoggedIn ? Visibility.Collapsed : Visibility.Visible;
            if (RegisterPanel != null && LoginPanel != null && LoginPanel.Visibility == Visibility.Visible)
            {
                RegisterPanel.Visibility = Visibility.Collapsed;
            }

            if (UserInfoPanel != null) UserInfoPanel.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;
            if (isLoggedIn && LoggedInUserText != null)
            {
                LoggedInUserText.Text = $"Bejelentkezve: {currentUser.Username}";
            }

            if (AddMoviePanel != null)
            {
                if (!isAdmin && AddMoviePanel.Visibility == Visibility.Visible)
                {
                    CancelEditMode();
                }
            }
            if (AddScreeningPanel != null)
            {
                if (!isAdmin && AddScreeningPanel.Visibility == Visibility.Visible)
                {
                    CancelAddScreeningButton_Click(null, null);
                }
            }
            if (AddNewMovieButton != null)
            {
                AddNewMovieButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            }
            if (AddNewScreeningButton != null)
            {
                AddNewScreeningButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async Task RefreshAllDataAsync()
        {
            await Task.WhenAll(LoadMoviesAsync(), LoadScreeningsAsync());
            PopulateScreeningMovieTitles();
            UpdateMovieUI();
            UpdateScreeningUI();
        }

        private async Task LoadMoviesAsync()
        {
            try
            {
                var res = await client.GetAsync("/api/movies/movies");
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync();
                    allMoviesCache = JsonConvert.DeserializeObject<List<Movie>>(json, jsonSerializerSettings) ?? new List<Movie>();
                }
                else
                {
                    MessageBox.Show($"Filmek betöltése sikertelen: {res.StatusCode} - {await res.Content.ReadAsStringAsync()}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                    allMoviesCache.Clear();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kivétel a filmek betöltése során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                allMoviesCache.Clear();
            }
        }

        private async Task LoadScreeningsAsync()
        {
            try
            {
                var res = await client.GetAsync("/api/screenings/screenings");
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync();
                    allScreeningsCache = JsonConvert.DeserializeObject<List<Screening>>(json, jsonSerializerSettings) ?? new List<Screening>();
                }
                else
                {
                    MessageBox.Show($"Vetítések betöltése sikertelen: {res.StatusCode} - {await res.Content.ReadAsStringAsync()}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                    allScreeningsCache.Clear();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kivétel a vetítések betöltése során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                allScreeningsCache.Clear();
            }
        }

        private void PopulateScreeningMovieTitles()
        {
            foreach (var screening in allScreeningsCache)
            {
                var movie = allMoviesCache.FirstOrDefault(m => m.Id == screening.MovieId);
                screening.MovieTitle = movie?.Title ?? "Ismeretlen Film";
            }
        }

        private void UpdateMovieUI()
        {
            MovieList.ItemsSource = null;
            MovieList.ItemsSource = allMoviesCache;
            if (SearchInput != null) SearchInput.Text = "";

            var filterMovies = new List<Movie> { new Movie { Id = 0, Title = "Összes film" } };
            filterMovies.AddRange(allMoviesCache);
            ScreeningFilterComboBox.ItemsSource = filterMovies;
            ScreeningFilterComboBox.SelectedIndex = 0;

            ScreeningMovieComboBox.ItemsSource = allMoviesCache;
        }

        private void UpdateScreeningUI(List<Screening> screeningsToDisplay = null)
        {
            ScreeningList.ItemsSource = null;
            ScreeningList.ItemsSource = screeningsToDisplay ?? allScreeningsCache.OrderBy(s => s.Time).ToList();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var loginRequest = new LoginRequest { EmailAddress = EmailLoginInput.Text, Password = PasswordLoginInput.Password };
            var jsonPayload = JsonConvert.SerializeObject(loginRequest, jsonSerializerSettings);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            try
            {
                var res = await client.PostAsync("/api/users/loginCheck", content);
                var responseJson = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    var loginResponse = JsonConvert.DeserializeObject<LoginResponse>(responseJson, jsonSerializerSettings);
                    if (loginResponse.Success && loginResponse.User != null && loginResponse.User.Id > 0)
                    {
                        authToken = loginResponse.Token;
                        currentUser = loginResponse.User;
                        MessageBox.Show(loginResponse.Message, "Sikeres bejelentkezés", MessageBoxButton.OK, MessageBoxImage.Information);
                        UpdateUIVisibility();
                        EmailLoginInput.Text = ""; PasswordLoginInput.Password = "";
                        await RefreshAllDataAsync();
                    }
                    else
                    {
                        string errMsg = loginResponse?.Message ?? "Ismeretlen hiba.";
                        if (loginResponse?.User == null || loginResponse.User.Id <= 0) errMsg += " (Hiányos felhasználói adatok)";
                        MessageBox.Show(errMsg, "Bejelentkezési hiba", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(responseJson, jsonSerializerSettings);
                    MessageBox.Show(errorResponse?.Message ?? $"Hiba: {res.StatusCode}", "Bejelentkezési hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kivétel a bejelentkezés során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (PasswordRegisterInput.Password != PasswordConfirmRegisterInput.Password)
            {
                MessageBox.Show("A megadott jelszavak nem egyeznek!", "Regisztrációs hiba", MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }
            var registerRequest = new RegisterRequest { Username = UsernameRegisterInput.Text, EmailAddress = EmailRegisterInput.Text, Password = PasswordRegisterInput.Password };
            var jsonPayload = JsonConvert.SerializeObject(registerRequest, jsonSerializerSettings);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            try
            {
                var res = await client.PostAsync("/api/users/register", content);
                var responseJson = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    var registerResponse = JsonConvert.DeserializeObject<RegisterResponse>(responseJson, jsonSerializerSettings);
                    if (registerResponse.Success)
                    {
                        MessageBox.Show(registerResponse.Message ?? "Sikeres regisztráció!", "Regisztráció", MessageBoxButton.OK, MessageBoxImage.Information);
                        UsernameRegisterInput.Text = ""; EmailRegisterInput.Text = ""; PasswordRegisterInput.Password = ""; PasswordConfirmRegisterInput.Password = "";
                        ShowLoginButton_Click(null, null);
                    }
                    else
                    {
                        string errorMessage = registerResponse.Message;
                        if (registerResponse.Messages != null && registerResponse.Messages.Count > 0) errorMessage = string.Join(Environment.NewLine, registerResponse.Messages);
                        MessageBox.Show(errorMessage ?? "Ismeretlen hiba.", "Regisztrációs hiba", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(responseJson, jsonSerializerSettings);
                    string errorMessage = errorResponse?.Message;
                    if (errorResponse?.Messages != null && errorResponse.Messages.Count > 0) errorMessage = string.Join(Environment.NewLine, errorResponse.Messages);
                    MessageBox.Show(errorMessage ?? $"Hiba: {res.StatusCode}", "Regisztrációs hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kivétel a regisztráció során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            authToken = null; currentUser = null; client.DefaultRequestHeaders.Authorization = null;
            UpdateUIVisibility();
            ClearMovieDetails();
            MessageBox.Show("Sikeresen kijelentkeztél.", "Kijelentkezés", MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshAllDataAsync();
            CancelEditMode();
            CancelAddScreeningButton_Click(null, null);
        }

        private async void MovieList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MovieList.SelectedItem is Movie selectedMovie)
            {
                if (selectedMovie.Id <= 0) { ClearMovieDetails(); return; }
                string requestUrl = $"/api/movies/movie-by-id/{selectedMovie.Id}";
                try
                {
                    var res = await client.GetAsync(requestUrl);
                    var responseContent = await res.Content.ReadAsStringAsync();
                    if (res.IsSuccessStatusCode)
                    {
                        var movie = JsonConvert.DeserializeObject<Movie>(responseContent, jsonSerializerSettings);
                        if (movie != null) { TitleText.Text = movie.Title; YearText.Text = movie.Year.ToString(); DescriptionText.Text = movie.Description; }
                        else { MessageBox.Show("Film feldolgozása sikertelen.", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error); ClearMovieDetails(); }
                    }
                    else { MessageBox.Show($"Film lekérése sikertelen: {res.StatusCode}\n{responseContent}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error); ClearMovieDetails(); }
                }
                catch (Exception ex) { MessageBox.Show($"Kivétel film lekérésekor: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error); ClearMovieDetails(); }
            }
            else { ClearMovieDetails(); }
        }

        private void ClearMovieDetails()
        {
            if (TitleText != null) TitleText.Text = "Nincs film kiválasztva";
            if (YearText != null) YearText.Text = "";
            if (DescriptionText != null) DescriptionText.Text = "";
        }

        private async void CreateOrUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentUser == null) { MessageBox.Show("Bejelentkezés szükséges.", "Hiba", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!currentUser.IsAdmin) { MessageBox.Show("Nincs jogosultságod!", "Hiba", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var title = TitleInput.Text; var description = DescriptionInput.Text; var imgUrl = ImgInput.Text;
            if (!int.TryParse(YearInput.Text, out int year) || year < 1800 || year > DateTime.Now.Year + 10)
            { MessageBox.Show("Érvénytelen év.", "Hiba", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(imgUrl))
            { MessageBox.Show("Minden mezőt ki kell tölteni.", "Hiányzó adatok", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            var moviePayload = new MoviePayload { Title = title, Description = description, Year = year, Img = imgUrl, AccountId = currentUser.Id };
            var jsonPayload = JsonConvert.SerializeObject(moviePayload, jsonSerializerSettings);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage res; string successMessage;

                if (editingMovieId.HasValue)
                {
                    res = await client.PutAsync($"/api/movies/movies/{editingMovieId.Value}", content);
                    successMessage = "Film sikeresen frissítve!";
                }
                else
                {
                    res = await client.PostAsync("/api/movies/movies", content);
                    successMessage = "Film sikeresen létrehozva!";
                }

                if (res.IsSuccessStatusCode)
                {
                    MessageBox.Show(successMessage, "Siker", MessageBoxButton.OK, MessageBoxImage.Information);
                    CancelEditMode();
                    await RefreshAllDataAsync();
                }
                else
                {
                    var errorContent = await res.Content.ReadAsStringAsync();
                    MessageBox.Show($"Művelet sikertelen: {res.StatusCode} - {errorContent}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kivétel a művelet során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditMovieButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentUser == null || !currentUser.IsAdmin)
            { MessageBox.Show("Nincs jogosultságod!", "Hiba", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            if (sender is Button button && button.DataContext is Movie movieToEdit)
            {
                SetEditMode(movieToEdit);
            }
        }

        private async void DeleteMovieButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentUser == null || !currentUser.IsAdmin)
            { MessageBox.Show("Nincs jogosultságod!", "Hiba", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            if (sender is Button button && button.DataContext is Movie movieToDelete)
            {
                if (MessageBox.Show($"Biztosan törlöd a '{movieToDelete.Title}' filmet és minden hozzá tartozó vetítést?", "Megerősítés", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    var deleteRequest = new MovieDeleteRequest { AccountId = currentUser.Id };
                    var jsonPayload = JsonConvert.SerializeObject(deleteRequest, jsonSerializerSettings);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/movies/movies/{movieToDelete.Id}") { Content = content };
                    try
                    {
                        var res = await client.SendAsync(request);
                        if (res.IsSuccessStatusCode)
                        {
                            MessageBox.Show("Film sikeresen törölve!");
                            await RefreshAllDataAsync();
                        }
                        else
                        {
                            var errorContent = await res.Content.ReadAsStringAsync();
                            MessageBox.Show($"Film törlése sikertelen: {res.StatusCode} - {errorContent}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex) { MessageBox.Show($"Kivétel a film törlése során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error); }
                }
            }
        }

        private void SearchInput_TextChanged(object sender, TextChangedEventArgs e) { PerformSearch(); }
        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchInput != null) SearchInput.Text = string.Empty;
        }

        private void PerformSearch()
        {
            if (SearchInput == null || MovieList == null || allMoviesCache == null) return;
            string searchTerm = SearchInput.Text.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(searchTerm)) { MovieList.ItemsSource = allMoviesCache; }
            else
            {
                var filteredMovies = allMoviesCache.Where(m =>
                    (m.Title?.ToLower().Contains(searchTerm) ?? false) ||
                    (m.Description?.ToLower().Contains(searchTerm) ?? false) ||
                    (m.Year.ToString().Contains(searchTerm))
                ).ToList();
                MovieList.ItemsSource = filteredMovies;
            }
        }

        private void ShowRegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (LoginPanel != null) LoginPanel.Visibility = Visibility.Collapsed;
            if (RegisterPanel != null) RegisterPanel.Visibility = Visibility.Visible;
        }

        private void ShowLoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (RegisterPanel != null) RegisterPanel.Visibility = Visibility.Collapsed;
            if (LoginPanel != null) LoginPanel.Visibility = Visibility.Visible;
        }

        private void SetEditMode(Movie movieToEdit = null)
        {
            if (currentUser == null || !currentUser.IsAdmin)
            {
                MessageBox.Show("Nincs jogosultságod ehhez a művelethez.", "Hiba", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AddMoviePanel.Visibility = Visibility.Visible;
            if (CancelEditButton != null) CancelEditButton.Visibility = Visibility.Visible;

            if (movieToEdit != null)
            {
                editingMovieId = movieToEdit.Id;
                AddMoviePanelTitle.Text = "Film szerkesztése";
                CreateOrUpdateButton.Content = "Módosítások mentése";
                TitleInput.Text = movieToEdit.Title;
                YearInput.Text = movieToEdit.Year.ToString();
                DescriptionInput.Text = movieToEdit.Description;
                ImgInput.Text = movieToEdit.Img;
            }
            else
            {
                editingMovieId = null;
                AddMoviePanelTitle.Text = "Új film hozzáadása";
                CreateOrUpdateButton.Content = "Létrehozás";
                TitleInput.Text = "";
                YearInput.Text = "";
                DescriptionInput.Text = "";
                ImgInput.Text = "";
            }
            if (AddMoviePanel.IsVisible) AddMoviePanel.BringIntoView();
        }

        private void CancelEditMode()
        {
            editingMovieId = null;
            AddMoviePanelTitle.Text = "Új film hozzáadása";
            CreateOrUpdateButton.Content = "Létrehozás";
            TitleInput.Text = "";
            YearInput.Text = "";
            DescriptionInput.Text = "";
            ImgInput.Text = "";
            AddMoviePanel.Visibility = Visibility.Collapsed;
            if (CancelEditButton != null) CancelEditButton.Visibility = Visibility.Collapsed;
        }

        private void AddNewMovieButton_Click(object sender, RoutedEventArgs e)
        {
            SetEditMode(null);
        }
        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            CancelEditMode();
        }

        private void ScreeningFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScreeningFilterComboBox.SelectedItem is Movie selectedMovie)
            {
                if (selectedMovie.Id == 0)
                {
                    UpdateScreeningUI();
                }
                else
                {
                    var filteredScreenings = allScreeningsCache
                        .Where(s => s.MovieId == selectedMovie.Id)
                        .OrderBy(s => s.Time)
                        .ToList();
                    UpdateScreeningUI(filteredScreenings);
                }
            }
        }

        private void AddNewScreeningButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentUser == null || !currentUser.IsAdmin)
            {
                MessageBox.Show("Nincs jogosultságod ehhez a művelethez.", "Hiba", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AddScreeningPanel.Visibility = Visibility.Visible;
            ScreeningMovieComboBox.SelectedIndex = -1;
            ScreeningRoomInput.Text = "";
            ScreeningTimeInput.Text = "";
        }

        private void CancelAddScreeningButton_Click(object sender, RoutedEventArgs e)
        {
            AddScreeningPanel.Visibility = Visibility.Collapsed;
        }

        private async void CreateScreeningButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentUser == null || !currentUser.IsAdmin)
            {
                MessageBox.Show("Nincs jogosultságod!", "Hiba", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ScreeningMovieComboBox.SelectedItem == null)
            {
                MessageBox.Show("Válassz filmet a vetítéshez!", "Hiányzó adat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(ScreeningRoomInput.Text))
            {
                MessageBox.Show("Add meg a terem nevét!", "Hiányzó adat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!DateTime.TryParse(ScreeningTimeInput.Text, out DateTime time))
            {
                MessageBox.Show("Érvénytelen dátum formátum! Használj 'ÉÉÉÉ-HH-NN ÓÓ:PP' formátumot.", "Formátum hiba", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedMovie = (Movie)ScreeningMovieComboBox.SelectedItem;

            var screeningPayload = new ScreeningPayload
            {
                MovieId = selectedMovie.Id,
                Room = ScreeningRoomInput.Text,
                Time = time,
                AccountId = currentUser.Id
            };

            var jsonPayload = JsonConvert.SerializeObject(screeningPayload, jsonSerializerSettings);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var res = await client.PostAsync("/api/screenings/screenings", content);

                if (res.IsSuccessStatusCode)
                {
                    MessageBox.Show("Vetítés sikeresen létrehozva!", "Siker", MessageBoxButton.OK, MessageBoxImage.Information);
                    CancelAddScreeningButton_Click(null, null);
                    await RefreshAllDataAsync();
                }
                else
                {
                    var errorContent = await res.Content.ReadAsStringAsync();
                    MessageBox.Show($"Vetítés létrehozása sikertelen: {res.StatusCode} - {errorContent}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kivétel a vetítés létrehozása során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #region Data Transfer Objects (DTOs) and Models

    public class User
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        public string Username { get; set; }
        public string EmailAddress { get; set; }
        public bool IsAdmin { get; set; }
    }

    public class LoginRequest
    {
        public string EmailAddress { get; set; }
        public string Password { get; set; }
    }

    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Token { get; set; }
        public User User { get; set; }
    }

    public class RegisterRequest
    {
        public string Username { get; set; }
        public string EmailAddress { get; set; }
        public string Password { get; set; }
    }

    public class RegisterResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> Messages { get; set; }
        public UserInfo User { get; set; }

        public class UserInfo
        {
            public int AccountId { get; set; }
            public string Username { get; set; }
            public string EmailAddress { get; set; }
        }
    }

    public class ErrorResponse
    {
        public string Message { get; set; }
        public List<string> Messages { get; set; }
        public bool? Success { get; set; }
    }

    public class MoviePayload
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public int Year { get; set; }
        public string Img { get; set; }
        public int AccountId { get; set; }
    }

    public class MovieDeleteRequest
    {
        public int AccountId { get; set; }
    }

    public class Movie
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int Year { get; set; }
        public string Img { get; set; }
        public string AdminName { get; set; }
    }

    public class Screening
    {
        public int Id { get; set; }
        public int MovieId { get; set; }
        public string Room { get; set; }
        public DateTime Time { get; set; }
        public string AdminName { get; set; }
        public string MovieTitle { get; set; }
        public string DisplayInfo => $"{MovieTitle} - {Room} terem - {Time:yyyy. MM. dd. HH:mm}";
    }

    public class ScreeningPayload
    {
        public int MovieId { get; set; }
        public string Room { get; set; }
        public DateTime Time { get; set; }
        public int AccountId { get; set; }
    }

    #endregion
}