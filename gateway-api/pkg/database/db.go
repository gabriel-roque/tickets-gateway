package database

import (
	"fmt"
	"log"
	"os"

	"gorm.io/driver/postgres"
	"gorm.io/gorm"
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
	db, err := CreateConnection()
	if err != nil {
		return false
	}

	sqlDB, err := db.DB()
	if err != nil {
		log.Printf("Erro ao obter a instância SQL do banco de dados: %v", err)
		return false
	}
	defer sqlDB.Close()

	err = sqlDB.Ping()
	if err != nil {
		log.Printf("Erro ao verificar a conexão com o banco de dados: %v", err)
		return false
	}

	log.Println("Banco de dados respondendo corretamente.")
	return true
}

func CreateConnection() (*gorm.DB, error) {
	_, connectionString := loadConfig()

	db, err := gorm.Open(postgres.Open(connectionString), &gorm.Config{})
	if err != nil {
		log.Printf("Erro ao conectar ao banco de dados: %v", err)
		return nil, err
	}

	log.Println("Conexão com o banco de dados estabelecida com sucesso.")
	return db, nil
}
