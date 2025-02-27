package database

import (
	"fmt"
	"log"
	"os"

	"github.com/jmoiron/sqlx"
	_ "github.com/lib/pq"
)

type DatabaseConfig struct {
	Host     string
	Port     string
	User     string
	Password string
	Dbname   string
}

func loadConfig() (DatabaseConfig, string) {
	config := DatabaseConfig{
		Host:     os.Getenv("DB_HOST"),
		Port:     os.Getenv("DB_PORT"),
		User:     os.Getenv("DB_USER"),
		Password: os.Getenv("DB_PASSWORD"),
		Dbname:   os.Getenv("DB_NAME"),
	}

	connectionString := fmt.Sprintf(
		"host=%s port=%s user=%s password=%s dbname=%s sslmode=disable",
		config.Host, config.Port, config.User, config.Password, config.Dbname,
	)

	return config, connectionString
}

func DatabasePing() bool {
	_, connectionString := loadConfig()

	db, err := sqlx.Open("postgres", connectionString)
	if err != nil {
		log.Fatalf("Erro ao conectar ao banco de dados: %v", err)
	}
	defer db.Close()

	err = db.Ping()
	if err != nil {
		log.Fatalf("Erro ao verificar a conexão com o banco de dados: %v", err)
	}

	fmt.Println("Conexão com o banco de dados estabelecida!")
	return true
}

func CreateConnection() *sqlx.DB {
	_, connectionString := loadConfig()

	db, err := sqlx.Open("postgres", connectionString)
	if err != nil {
		log.Fatalf("Erro ao conectar ao banco de dados: %v", err)
	}

	return db
}
