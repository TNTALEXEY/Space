#if WINDOWS
using GorzdravParser;
#elif ANDROID
using Android.Hardware;

#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Xml;
using static GorzdravParserCore.ExceptionsCore;
using static GorzdravParserCore.Logger;

namespace GorzdravParserCore
{

	public class ParserLogic : IDisposable
	{
#if WINDOWS
		//ВАЖНО парсерлогик ГАРАНТИРОВАНО должен вызвать форму, чтоб через него можно было с ней взаимодействовать
		readonly GorViewer gorviewer = null;
		//переносим медиков на уровень выше без нарушения логики
		public List<List<string>> medicks = new List<List<string>>();
		public ParserLogic()
		{
			gorviewer = new GorViewer();
			gorviewer.Assignmedicks(medicks);
		}
		public void Show()
		{
			gorviewer.Show();
		}
		public void Dispose()
		{
			gorviewer.Dispose();
			//здесь будем освобождать всякое (защиты от работы после диспозе не будет, ибо нет необходимости тут такое делать)
		}
		public async Task OpenPage(string link)
		{
			await gorviewer.OpenPage(link);
		}
		public async Task<bool> ParseWebPage()
		{
			return await gorviewer.ParseWebPage();
		}
		public void Close()
		{
			gorviewer.Close();
		}
#elif ANDROID
		public List<List<string>> medicks = new List<List<string>>();
		Microsoft.Maui.Controls.WebView browser = null;
		VerticalStackLayout stlaMain = null;
		public void passStlaMain(VerticalStackLayout stlaTemp)
		{ stlaMain = stlaTemp; }
		public void Close()
		{
			if (stlaMain.Children.Contains(browser))
			{
				stlaMain.Children.Remove(browser);
			}
			//так как не было show то и close не нужен
		}
		public void Dispose()
		{
			browser.Source = null;
			if (browser.Handler != null)
			{
				// Это высвобождает нативные ресурсы Android
				browser.Handler.DisconnectHandler();
			}
			browser = null; // Теперь GC может забрать объект
			//Debug.WriteLine($"disposed everything");
		}
		public void Show()
		{
			//нужен для работы пк версии бота
		}
		public async Task OpenPage(string link)
		{
			WriteLog($"Начато открытие страницы {link}");
			browser = new WebView
			{
				Source = link,
				HeightRequest = 200,
				WidthRequest = 200,
				//IsVisible = false
			};
			
			//webset.
			WriteLog($"Страница открыта {link}, открытие её браузера");
			stlaMain.Children.Add(browser);
			WriteLog($"Страница браузер открыта {link}");
		}
		public async Task<bool> ParseWebPage()
		{
			WriteLog("Начат парсинг страницы");
			//formView gimmeData = this.Owner as formView;
			medicks.Clear();//очищаем ибо нам данные нужны были до этого
			
			string html = null;
			string state = await browser.EvaluateJavaScriptAsync("document.readyState");
			if (state == "complete")
				html = await browser.EvaluateJavaScriptAsync("document.documentElement.outerHTML;");
			// Превращаем Html в строку
			WriteLog("Контент страницы получен, запуск парсера в другом потоке");
			return await Task.Run<bool>(() => ParserThread(html));
		}

		private bool ParserThread(string html)
		{
			WriteLog("Запущен парсер в другом потоке");
			string docName = "";
			string talonAmount = "";
			byte readstate = 0;
			bool readdone = false;//если true то исполняет логику проверки значений и бла бла
								  //забираем html
								  // 2. Обработка результата
			WriteLog("Десериализация строки");
			if (!string.IsNullOrEmpty(html))
			{
				// Попробуем десериализовать как JSON (WebView2)
				try
				{
					html = System.Text.Json.JsonSerializer.Deserialize<string>(html);
				}
				catch { /* не JSON — оставляем как есть */ }

				// Заменяем оставшиеся эскейпы (Android)
				html = html.Replace("\\u003C", "<")
						   .Replace("\\u003E", ">")
						   .Replace("\\u0026", "&")
						   .Replace("\\\"", "\"");
			}
			WriteLog("Десериализация окончена, формирование массива");
			// 3. Разбираем строку на массив
			string[] htmlmass = { "" };
			if (!string.IsNullOrEmpty(html))
			{
				htmlmass = html.Split(new[] { "\\r\\n", "\\n" }, StringSplitOptions.None);
			}
			//foreach(string str in htmlmass)
			//Debug.WriteLine($"{str}");
			//парсим html с целью найти всех врачей
			WriteLog("Массив сформирован, начат парсинг");
			for (int i = 0; i < htmlmass.Length; i++)
			{
				switch (readstate)
				{
					case 0://ищет "doctorsOutput"> и переходит на следующий этап если находит
						if (htmlmass[i].Contains("\"doctorsOutput\">"))
						{
							WriteLog($"Найдено начало списка врачей");
							readstate = 1;
						}

						break;
					case 1://ищет service-doctor__title"> и забирает всё от него до </div> в этой строке
						if (htmlmass[i].Contains("service-doctor__title\">"))
						{
							docName = FetchBetweenThings(htmlmass[i]);
							readstate = 2;
							WriteLog($"Найден врач {docName}");
						}
						break;
					case 2://ищет service-doctor-top__list_numbers"> и переходит на 3 если нашёл
						if (htmlmass[i].Contains("service-doctor-top__list_numbers\">"))
							readstate = 3;
						break;
					case 3://ищет <li> (для сбора строки (переход на 4)) или </ul> (для завершения чтения и перехода на 1) 
						if (htmlmass[i].Contains("<li>"))
						{
							if (htmlmass[i].Contains("Доступных номерков:"))
							{
								talonAmount = FetchBetweenThings(htmlmass[i]);
								talonAmount = new string(talonAmount.Where(Char.IsDigit).ToArray());//делаем строку только числовой
								WriteLog($"Количество номерков {talonAmount}");
								readdone = true;
								readstate = 1;
							}
						}
						else if (htmlmass[i].Contains("</ul>"))
						{
							talonAmount = "0";
							WriteLog($"Количество номерков {talonAmount}");
							readdone = true;
							readstate = 1;
						}
						break;
					default://невозможный ридстейт делает нулём
						WriteLog("Встречен невозможный ридстейт", 1);
						readstate = 0;
						break;
				}
				if (readdone)
				{
					WriteLog("Чтение врача окончено, проверка на конечность");
					//здесь делаем проверку что мы встретили шаблон для полного завершения чтения
					if (docName == "{{:name}}")
					{
						//проверка что размер листа не 0 ибо иначе нужно уйти на повтор
						if (medicks.Count != 0)
						{
							WriteLog("Найдены врачи, завершение потока парсера");
							return true;

						}
						else
						{
							WriteLog("Не найдены врачи, завершение потока парсера");
							return false;
						}
					}
					else
					{
						//логика подготовки к след анализу
						//т.е. проверка данных на действительность и добавление в лист
						WriteLog("Проверка валидности данных о враче");
						if (!string.IsNullOrEmpty(docName) && !string.IsNullOrEmpty(talonAmount))
						{
							medicks.Add(new List<string> { docName, talonAmount });
							readdone = false;
							WriteLog($"Врач {docName} валиден, количество его талонов {talonAmount}");
						}
						else
						{
							WriteLog("Врач не валиден и парсер возвращает исключение");
							throw new BadParserDataException("BadParse");//особый случай поэтому без обработки
						}
					}
				}
			}
			WriteLog("Парсер ничего не нашёл (т.е. нет блока врачей)");
			return false;//если вообще ничего не нашёл на странице то false
		}

		private string FetchBetweenThings(string fetchstring)//извлекает что либо между > и < (если по невозможной причине дадут плохое вернёт ничего)
		{
			WriteLog("Фетчер запущен");
			string returner = "";
			//проще недумать и собирать строку между ><
			byte readstate = 0;
			foreach (char letter in fetchstring)
			{
				switch (readstate)
				{
					case 0://ищет >
						if (letter == '>')
							readstate = 1;
						break;
					case 1://собирает строку и ищет <
						if (letter != '<')
						{
							returner += letter;
						}
						else
						{
							readstate = 2;
						}
						break;
					case 2://ничего не делает нужен чтобы пропустить остаток строки с </div>
						break;
					default://невозможный ридстейт делает нулём
						readstate = 0;
						break;
				}
			}
			WriteLog($"Фетчер возвращает {returner}");
			return returner;
		}
#endif
	}
}