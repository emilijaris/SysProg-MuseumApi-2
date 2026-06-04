import threading
import requests
import time

#odbrana
#1.num_reques=1, samo jedan poziv python skripte, prikaz potrebnog vremena izvrsenja pri prvom pribavljanju podataka sa servera
#2.num_reques=1, jos jedan poziv python skripte kako bi se ilustrovalo skraceno vreme pribavljanja istog podatkaa, ali sada iz kesa
#3.num_request=10 queries= ["sunflower","gold", "egypt", "monet", "statue"], pribavljanje po 10 slika, kako bi se prkazao rad servera za vise istovremenih zahteva
#4.num_request=10 queries= ["sunflower","gold", "egypt", "monet", "statue"], pribavljanje po 20 slika, neke slike ce biti zabranjene

URL = "http://localhost:8080/"
NUM_REQUESTS = 10
QUERIES = ["sunflower","gold", "egypt"]#, "monet", "statue", "glass"]#, "roman", "silk","lilies"]#,"landscape","garden","forest","roses","woman","man","child","portrait","dancer","cat","dog","horse","bird","lion","dragon","mirror"]

def send_request(query):
    try:
        response = requests.get(f"{URL}?q={query}", timeout=120)
        if response.status_code == 200:
            data = response.json()
            print(f"[TEST] Query: {query:<10} | Status: 200 | Paintings: {len(data)}")
        else:
            print(f"[FAIL] Query: {query:<10} | Status: {response.status_code}")
    except Exception as e:
        print(f"[ERROR] Query {query}: {e}")

def run_stress_test():
    print(f"--- POKRETANJE STRESS TESTA ({NUM_REQUESTS} PARALELNIH UPITA) ---")
    threads = []
    
    for i in range(NUM_REQUESTS):
        query = QUERIES[i % len(QUERIES)]
        t = threading.Thread(target=send_request, args=(query,))
        threads.append(t)
        t.start()
        time.sleep(0.2)
    for t in threads:
        t.join()
    print("--- TEST ZAVRŠEN ---")

if __name__ == "__main__":
    run_stress_test()