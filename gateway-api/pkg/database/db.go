package database

import (
	"fmt"
	"log"
	"os"
	"sync"
	"time"

	transaction_pix "github.com/gabriel-roque/gateway-api/internal/transaction-pix"
	"gorm.io/driver/postgres"
	"gorm.io/gorm"
)

var (
	connectionMu sync.Mutex
	connection   *gorm.DB
)

func Migrate() error {
	var lastError error
	for attempt := 0; attempt < 30; attempt++ {
		db, err := CreateConnection()
		if err == nil {
			err = migrateConnection(db)
		}
		if err == nil {
			return nil
		}
		lastError = err
		time.Sleep(2 * time.Second)
	}
	return fmt.Errorf("migration did not complete after retries: %w", lastError)
}

func migrateConnection(db *gorm.DB) error {
	if !db.Migrator().HasTable(&transaction_pix.TransactionPix{}) {
		return db.AutoMigrate(&transaction_pix.TransactionPix{})
	}

	return db.Transaction(func(tx *gorm.DB) error {
		var duplicateExternalIDs []string
		if err := tx.Model(&transaction_pix.TransactionPix{}).
			Select("external_id").
			Group("external_id").
			Having("COUNT(*) > 1").
			Pluck("external_id", &duplicateExternalIDs).Error; err != nil {
			return err
		}
		for _, externalID := range duplicateExternalIDs {
			var records []transaction_pix.TransactionPix
			if err := tx.Where("external_id = ?", externalID).Order("id").Find(&records).Error; err != nil {
				return err
			}
			for _, duplicate := range records[1:] {
				if err := tx.Delete(&duplicate).Error; err != nil {
					return err
				}
			}
		}
		if !tx.Migrator().HasIndex(&transaction_pix.TransactionPix{}, "ExternalId") {
			return tx.Migrator().CreateIndex(&transaction_pix.TransactionPix{}, "ExternalId")
		}
		return nil
	})
}

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
	err = sqlDB.Ping()
	if err != nil {
		log.Printf("Erro ao verificar a conexão com o banco de dados: %v", err)
		return false
	}

	log.Println("Banco de dados respondendo corretamente.")
	return true
}

func CreateConnection() (*gorm.DB, error) {
	connectionMu.Lock()
	defer connectionMu.Unlock()
	if connection != nil {
		return connection, nil
	}
	_, connectionString := loadConfig()

	db, err := gorm.Open(postgres.Open(connectionString), &gorm.Config{})
	if err != nil {
		log.Printf("Erro ao conectar ao banco de dados: %v", err)
		return nil, err
	}

	sqlDB, _ := db.DB()
	sqlDB.SetMaxOpenConns(10)
	sqlDB.SetMaxIdleConns(5)
	sqlDB.SetConnMaxLifetime(30 * time.Minute)

	log.Println("Conexão com o banco de dados estabelecida com sucesso.")
	connection = db
	return connection, nil
}

func CloseConnection() error {
	connectionMu.Lock()
	defer connectionMu.Unlock()
	if connection == nil {
		return nil
	}
	sqlDB, err := connection.DB()
	if err != nil {
		return err
	}
	connection = nil
	return sqlDB.Close()
}
