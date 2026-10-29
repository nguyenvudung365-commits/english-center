pipeline {
    agent { label 'dotnet10-docker' }
    options {
        skipDefaultCheckout(true)
        disableConcurrentBuilds()
        timeout(time: 40, unit: 'MINUTES')
    }
    parameters {
        booleanParam(name: 'DEPLOY', defaultValue: false, description: 'Triển khai lên Docker host của agent, chỉ dành cho main.')
    }
    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO = '1'
    }
    stages {
        stage('Lấy mã nguồn') {
            steps { checkout scm }
        }
        stage('Khôi phục và biên dịch') {
            steps {
                sh 'dotnet restore EnglishCenter.sln'
                sh 'dotnet build EnglishCenter.sln -c Release --no-restore'
            }
        }
        stage('Kiểm thử') {
            steps {
                sh 'dotnet test EnglishCenter.sln -c Release --no-build --logger trx --results-directory TestResults'
            }
        }
        stage('Kiểm tra container và PostgreSQL') {
            steps { sh 'bash scripts/ci-smoke.sh' }
        }
        stage('Build image bàn giao') {
            when { branch 'main' }
            steps {
                script {
                    env.IMAGE_TAG = sh(script: 'git rev-parse HEAD', returnStdout: true).trim()
                }
                sh 'docker build -t "english-center-api:$IMAGE_TAG" .'
            }
        }
        stage('Triển khai') {
            when { allOf { branch 'main'; expression { params.DEPLOY } } }
            steps {
                // Chỉ job main đáng tin cậy được cấp credential này.
                withCredentials([file(credentialsId: 'english-center-deploy-env', variable: 'DEPLOY_ENV')]) {
                    sh 'docker compose --env-file "$DEPLOY_ENV" --project-name english-center -f compose.yaml config --quiet'
                    sh 'docker compose --env-file "$DEPLOY_ENV" --project-name english-center -f compose.yaml up -d --no-build --wait --wait-timeout 180'
                }
            }
        }
    }
    post {
        always {
            archiveArtifacts artifacts: 'TestResults/**/*.trx', allowEmptyArchive: true
        }
    }
}
