package env

import (
	"log"

	"github.com/joho/godotenv"
)

func LoadEnvs() {
	err := godotenv.Load()
	if err != nil {
		log.Println("Arquivo .env não encontrado, seguindo com variáveis de ambiente do sistema")
	}
}
