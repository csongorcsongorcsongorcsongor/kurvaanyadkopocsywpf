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
    /// <summary>
    /// Az alkalmazás fő ablakának logikája (code-behind).
    /// Ez az osztály felelős a felhasználói felület (UI) eseményeinek kezeléséért,
    /// az adatok API-n keresztüli lekérdezéséért és küldéséért, valamint az alkalmazás
    /// általános állapotának (pl. bejelentkezett felhasználó) menedzseléséért.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Mezők és Tulajdonságok

        // HttpClient objektum, ami az API hívásokat kezeli.
        // A BaseAddress beállítása leegyszerűsíti a kéréseket, mert nem kell minden alkalommal a teljes URL-t megadni.
        private readonly HttpClient client = new HttpClient { BaseAddress = new Uri("http://localhost:4444") };

        // Gyorsítótárak (cache) a szerverről letöltött adatok tárolására.
        // Ezzel elkerüljük a felesleges hálózati kéréseket, és a kliensoldali műveletek (pl. szűrés) gyorsabbak lesznek.
        private List<Movie> allMoviesCache = new List<Movie>();
        private List<Screening> allScreeningsCache = new List<Screening>();

        // Beállítások a Newtonsoft.Json csomaghoz, ami a C# objektumok és a JSON stringek közötti átalakítást végzi.
        // A CamelCasePropertyNamesContractResolver biztosítja, hogy a C# PascalCase (Pl: MovieId) 
        // és a JavaScript/JSON camelCase (pl: movieId) elnevezési konvenciók kompatibilisek legyenek.
        private readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore // A null értékű tulajdonságokat figyelmen kívül hagyja a JSON létrehozásakor.
        };

        // Az aktuális felhasználói munkamenet (session) adatai.
        private string authToken = null; // A bejelentkezés után kapott JWT token, amit a hitelesítést igénylő API hívásokhoz használunk.
        private User currentUser = null; // A bejelentkezett felhasználó adatai (ID, név, admin jogosultság).
        private int? editingMovieId = null; // Annak a filmnek az ID-ja, amit éppen szerkesztünk. Null, ha új filmet hozunk létre.

        #endregion

        #region Inicializálás

        /// <summary>
        /// A MainWindow konstruktora. Ez a metódus fut le legelőször, amikor az ablak létrejön.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent(); // A XAML-ben definiált UI elemek inicializálása (pl. gombok, listák).
            InitializeApplicationState(); // Az alkalmazás logikai kezdőállapotának beállítása.
        }

        /// <summary>
        /// Az alkalmazás kezdeti állapotát állítja be: elrejti a dinamikus paneleket és betölti a kezdeti adatokat a szerverről.
        /// Az `async void` használata itt elfogadott, mert ez egy eseménykezelő-szerű "top-level" metódus.
        /// </summary>
        private async void InitializeApplicationState()
        {
            UpdateUIVisibility();
            RegisterPanel.Visibility = Visibility.Collapsed;
            AddMoviePanel.Visibility = Visibility.Collapsed;
            AddScreeningPanel.Visibility = Visibility.Collapsed;
            CancelEditButton.Visibility = Visibility.Collapsed;

            // Elindítja a filmek és vetítések betöltését a szerverről.
            await RefreshAllDataAsync();
        }

        #endregion

        #region UI és Adatkezelés

        /// <summary>
        /// Frissíti a felhasználói felület (UI) különböző részeinek láthatóságát a bejelentkezési állapot
        /// és a felhasználói jogosultságok (pl. admin) alapján.
        /// </summary>
        private void UpdateUIVisibility()
        {
            bool isLoggedIn = currentUser != null;
            bool isAdmin = isLoggedIn && currentUser.IsAdmin;

            // Bejelentkezési/regisztrációs és felhasználói információs panelek láthatóságának váltása.
            LoginPanel.Visibility = isLoggedIn ? Visibility.Collapsed : Visibility.Visible;
            if (RegisterPanel.Visibility == Visibility.Visible && isLoggedIn)
            {
                RegisterPanel.Visibility = Visibility.Collapsed;
            }
            UserInfoPanel.Visibility = isLoggedIn ? Visibility.Visible : Visibility.Collapsed;
            if (isLoggedIn)
            {
                LoggedInUserText.Text = $"Bejelentkezve: {currentUser.Username}";
            }

            // Adminisztrátori panelek és gombok láthatóságának beállítása.
            // Ha a felhasználó nem admin, és egy admin panel mégis látható, elrejtjük.
            if (!isAdmin)
            {
                if (AddMoviePanel.Visibility == Visibility.Visible) CancelEditMode();
                if (AddScreeningPanel.Visibility == Visibility.Visible) CancelAddScreeningButton_Click(null, null);
            }
            AddNewMovieButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            AddNewScreeningButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Központi adatfrissítő függvény. Párhuzamosan lekéri a filmeket és a vetítéseket a szerverről,
        /// majd frissíti a teljes felhasználói felületet, hogy az adatok konzisztensek legyenek.
        /// </summary>
        private async Task RefreshAllDataAsync()
        {
            // A Task.WhenAll megvárja, amíg mindkét aszinkron hívás (LoadMoviesAsync és LoadScreeningsAsync) befejeződik.
            // Ez gyorsabb, mintha egymás után hívnánk őket.
            await Task.WhenAll(LoadMoviesAsync(), LoadScreeningsAsync());

            // Miután mindkét lista (filmek és vetítések) betöltődött a cache-be, összekapcsoljuk őket a kliens oldalon.
            PopulateScreeningMovieTitles();

            // Végül frissítjük a UI-t a friss, feldolgozott adatokkal.
            UpdateMovieUI();
            UpdateScreeningUI();
        }

        /// <summary>
        /// Aszinkron módon lekéri a filmek listáját az API-tól és eltárolja a `allMoviesCache`-ben.
        /// </summary>
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

        /// <summary>
        /// Aszinkron módon lekéri a vetítések listáját az API-tól és eltárolja a `allScreeningsCache`-ben.
        /// </summary>
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

        /// <summary>
        /// Hozzárendeli a filmek címeit a vetítésekhez a `movieId` alapján.
        /// Ez egy kliensoldali "join", ami elkerüli a felesleges API hívásokat, mivel a filmek már a `allMoviesCache`-ben vannak.
        /// </summary>
        private void PopulateScreeningMovieTitles()
        {
            foreach (var screening in allScreeningsCache)
            {
                var movie = allMoviesCache.FirstOrDefault(m => m.Id == screening.MovieId);
                screening.MovieTitle = movie?.Title ?? "Ismeretlen Film";
            }
        }

        /// <summary>
        /// Frissíti a filmekkel kapcsolatos UI elemeket: a filmek listáját, a keresőmezőt,
        /// és feltölti a vetítésekhez tartozó szűrőt és a létrehozó legördülő menüt a friss film-listával.
        /// </summary>
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

        /// <summary>
        /// Frissíti a vetítések listáját a UI-on.
        /// Paraméterként megadható egy szűrt lista (pl. egy filmhez tartozó vetítések).
        /// Ha nincs paraméter, a teljes, időrendbe rendezett vetítés-listát jeleníti meg.
        /// </summary>
        private void UpdateScreeningUI(List<Screening> screeningsToDisplay = null)
        {
            ScreeningList.ItemsSource = null;
            ScreeningList.ItemsSource = screeningsToDisplay ?? allScreeningsCache.OrderBy(s => s.Time).ToList();
        }

        #endregion

        #region Felhasználói Műveletek (Login, Register, Logout)

        /// <summary>
        /// A "Bejelentkezés" gomb eseménykezelője. Elküldi a bejelentkezési adatokat az API-nak.
        /// </summary>
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

        /// <summary>
        /// A "Regisztráció" gomb eseménykezelője. Elküldi az új felhasználó adatait az API-nak.
        /// </summary>
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

        /// <summary>
        /// A "Kijelentkezés" gomb eseménykezelője. Törli a munkamenet adatait és frissíti a UI-t.
        /// </summary>
        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            authToken = null;
            currentUser = null;
            client.DefaultRequestHeaders.Authorization = null;
            UpdateUIVisibility();
            ClearMovieDetails();
            MessageBox.Show("Sikeresen kijelentkeztél.", "Kijelentkezés", MessageBoxButton.OK, MessageBoxImage.Information);
            await RefreshAllDataAsync();
            CancelEditMode();
            CancelAddScreeningButton_Click(null, null);
        }

        #endregion

        #region Film Műveletek (CRUD - Létrehozás, Olvasás, Frissítés, Törlés)

        /// <summary>
        /// A filmek listájában (MovieList) történő kiválasztás eseménykezelője. Betölti a kiválasztott film részleteit.
        /// </summary>
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

        /// <summary>
        /// Kiüríti a film részleteit megjelenítő UI mezőket.
        /// </summary>
        private void ClearMovieDetails()
        {
            if (TitleText != null) TitleText.Text = "Nincs film kiválasztva";
            if (YearText != null) YearText.Text = "";
            if (DescriptionText != null) DescriptionText.Text = "";
        }

        /// <summary>
        /// A film létrehozó/módosító panel "Mentés" gombjának eseménykezelője.
        /// A `editingMovieId` alapján dönti el, hogy új filmet hoz létre (POST) vagy meglévőt frissít (PUT).
        /// </summary>
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

        /// <summary>
        /// A filmek listájában lévő "Szerkesztés" gomb eseménykezelője.
        /// A `DataContext` segítségével azonosítja a szerkesztendő filmet.
        /// </summary>
        private void EditMovieButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentUser == null || !currentUser.IsAdmin)
            { MessageBox.Show("Nincs jogosultságod!", "Hiba", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            if (sender is Button button && button.DataContext is Movie movieToEdit)
            {
                SetEditMode(movieToEdit);
            }
        }

        /// <summary>
        /// A filmek listájában lévő "Törlés" gomb eseménykezelője.
        /// </summary>
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

        /// <summary>
        /// Előkészíti és megjeleníti a film létrehozó/szerkesztő panelt.
        /// Ha `movieToEdit` null, akkor új film létrehozására készül, egyébként a megadott film adataival tölti fel a mezőket.
        /// </summary>
        private void SetEditMode(Movie movieToEdit = null)
        {
            if (currentUser == null || !currentUser.IsAdmin)
            {
                MessageBox.Show("Nincs jogosultságod ehhez a művelethez.", "Hiba", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AddMoviePanel.Visibility = Visibility.Visible;
            CancelEditButton.Visibility = Visibility.Visible;

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
            AddMoviePanel.BringIntoView();
        }

        /// <summary>
        /// Visszavonja a film szerkesztési módot, elrejti a panelt és alaphelyzetbe állítja a változókat.
        /// </summary>
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
            CancelEditButton.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Az "Új film hozzáadása" gomb eseménykezelője.
        /// </summary>
        private void AddNewMovieButton_Click(object sender, RoutedEventArgs e)
        {
            SetEditMode(null);
        }

        /// <summary>
        /// A "Mégsem" gomb eseménykezelője a film szerkesztő panelen.
        /// </summary>
        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            CancelEditMode();
        }

        #endregion

        #region Vetítés Műveletek

        /// <summary>
        /// A vetítések szűrő ComboBox-ának eseménykezelője.
        /// A kiválasztott film alapján szűri a `ScreeningList` tartalmát.
        /// </summary>
        private void ScreeningFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScreeningFilterComboBox.SelectedItem is Movie selectedMovie)
            {
                if (selectedMovie.Id == 0) // "Összes film" opció
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

        /// <summary>
        /// Az "Új vetítés hozzáadása" gomb eseménykezelője. Megjeleníti a létrehozó panelt.
        /// </summary>
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

        /// <summary>
        /// A "Mégsem" gomb eseménykezelője a vetítés létrehozó panelen. Elrejti a panelt.
        /// </summary>
        private void CancelAddScreeningButton_Click(object sender, RoutedEventArgs e)
        {
            AddScreeningPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// A "Vetítés létrehozása" gomb eseménykezelője. Ellenőrzi az adatokat,
        /// majd elküldi az új vetítés adatait az API-nak.
        /// </summary>
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

        #endregion

        #region Keresés és UI Váltás

        /// <summary>
        /// A keresőmező szövegének változását kezeli. Minden billentyűleütésre szűri a listát.
        /// </summary>
        private void SearchInput_TextChanged(object sender, TextChangedEventArgs e) { PerformSearch(); }

        /// <summary>
        /// A keresőmező melletti "X" gomb eseménykezelője. Törli a keresési feltételt.
        /// </summary>
        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchInput != null) SearchInput.Text = string.Empty;
        }

        /// <summary>
        /// Elvégzi a tényleges keresést a `allMoviesCache`-ben a `SearchInput` tartalma alapján.
        /// </summary>
        private void PerformSearch()
        {
            if (SearchInput == null || MovieList == null || allMoviesCache == null) return;
            string searchTerm = SearchInput.Text.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                MovieList.ItemsSource = allMoviesCache;
            }
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

        /// <summary>
        /// A bejelentkezési panelen lévő "Regisztrációra váltás" gomb eseménykezelője.
        /// </summary>
        private void ShowRegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (LoginPanel != null) LoginPanel.Visibility = Visibility.Collapsed;
            if (RegisterPanel != null) RegisterPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// A regisztrációs panelen lévő "Bejelentkezésre váltás" gomb eseménykezelője.
        /// </summary>
        private void ShowLoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (RegisterPanel != null) RegisterPanel.Visibility = Visibility.Collapsed;
            if (LoginPanel != null) LoginPanel.Visibility = Visibility.Visible;
        }

        #endregion
    }

    #region Data Transfer Objects (DTOs) and Models

    // Ezek az osztályok határozzák meg az adatok struktúráját a C# alkalmazásban.
    // A [JsonProperty] attribútum segít a JSON kulcsok és a C# tulajdonságnevek közötti megfeleltetésben,
    // ha azok eltérnek (pl. "id" vs "Id").

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
        // Ez a tulajdonság csak a kliensoldalon létezik a könnyebb megjelenítés érdekében.
        public string MovieTitle { get; set; }
        // Ez a "számított" tulajdonság egy formázott stringet ad vissza a UI-on való megjelenítéshez.
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